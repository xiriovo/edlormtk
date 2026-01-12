using System;
using System.Threading;
using System.Threading.Tasks;
using tools.Modules.Common;

namespace tools.Modules.Qualcomm
{
    /// <summary>
    /// 设备状态变化事件参数
    /// </summary>
    public class DeviceStateChangedEventArgs : EventArgs
    {
        public DeviceProtocolState OldState { get; set; }
        public DeviceProtocolState NewState { get; set; }
        public DeviceStateInfo StateInfo { get; set; } = new();
        public string Message { get; set; } = "";
    }

    /// <summary>
    /// 自动修复结果
    /// </summary>
    public class AutoRecoveryResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public DeviceProtocolState NewState { get; set; }
        public string ActionTaken { get; set; } = "";
    }

    /// <summary>
    /// 设备状态管理器
    /// 自动检测状态、处理端口堵死、执行恢复操作
    /// </summary>
    public class DeviceStateManager : IDisposable
    {
        private readonly SerialPortManager _port;
        private readonly Action<string>? _log;
        private DeviceStateDetector? _detector;
        private DeviceProtocolState _currentState = DeviceProtocolState.Unknown;
        private CancellationTokenSource? _monitorCts;
        private bool _isMonitoring;
        private bool _disposed;

        /// <summary>状态变化事件</summary>
        public event EventHandler<DeviceStateChangedEventArgs>? StateChanged;

        /// <summary>当前状态</summary>
        public DeviceProtocolState CurrentState => _currentState;

        /// <summary>最后一次状态信息</summary>
        public DeviceStateInfo? LastStateInfo { get; private set; }

        public DeviceStateManager(SerialPortManager port, Action<string>? log = null)
        {
            _port = port ?? throw new ArgumentNullException(nameof(port));
            _log = log;
        }

        /// <summary>
        /// 初始化并检测状态
        /// </summary>
        public async Task<DeviceStateInfo> InitializeAsync(CancellationToken ct = default)
        {
            _detector = new DeviceStateDetector(_port, _log);
            
            var stateInfo = await _detector.DetectStateAsync(ct);
            UpdateState(stateInfo);
            
            return stateInfo;
        }

        /// <summary>
        /// 快速检测 (不发送任何命令)
        /// </summary>
        public DeviceStateInfo QuickDetect()
        {
            _detector ??= new DeviceStateDetector(_port, _log);
            var stateInfo = _detector.QuickDetect();
            UpdateState(stateInfo);
            return stateInfo;
        }

        /// <summary>
        /// 开始状态监控
        /// </summary>
        public void StartMonitoring(int intervalMs = 1000)
        {
            if (_isMonitoring) return;
            
            _monitorCts = new CancellationTokenSource();
            _isMonitoring = true;
            
            _ = MonitorLoopAsync(intervalMs, _monitorCts.Token);
        }

        /// <summary>
        /// 停止状态监控
        /// </summary>
        public void StopMonitoring()
        {
            _monitorCts?.Cancel();
            _isMonitoring = false;
        }

        /// <summary>
        /// 监控循环
        /// </summary>
        private async Task MonitorLoopAsync(int intervalMs, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_port.IsOpen)
                    {
                        var stateInfo = QuickDetect();
                        
                        // 检查是否需要自动修复
                        if (ShouldAttemptRecovery(stateInfo))
                        {
                            await AttemptAutoRecoveryAsync(stateInfo, ct);
                        }
                    }
                    
                    await Task.Delay(intervalMs, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"[状态监控] 异常: {ex.Message}");
                }
            }
            
            _isMonitoring = false;
        }

        /// <summary>
        /// 更新状态
        /// </summary>
        private void UpdateState(DeviceStateInfo stateInfo)
        {
            var oldState = _currentState;
            _currentState = stateInfo.State;
            LastStateInfo = stateInfo;
            
            if (oldState != _currentState)
            {
                _log?.Invoke($"[状态变化] {DeviceStateDetector.GetStateDisplayText(oldState)} -> {DeviceStateDetector.GetStateDisplayText(_currentState)}");
                
                StateChanged?.Invoke(this, new DeviceStateChangedEventArgs
                {
                    OldState = oldState,
                    NewState = _currentState,
                    StateInfo = stateInfo,
                    Message = stateInfo.Description
                });
            }
        }

        /// <summary>
        /// 检查是否需要自动修复
        /// </summary>
        private bool ShouldAttemptRecovery(DeviceStateInfo stateInfo)
        {
            return stateInfo.State switch
            {
                DeviceProtocolState.NoResponse => true,
                DeviceProtocolState.FirehoseConfigureFailed => true,
                DeviceProtocolState.PortError => true,
                _ => false
            };
        }

        /// <summary>
        /// 尝试自动恢复
        /// </summary>
        public async Task<AutoRecoveryResult> AttemptAutoRecoveryAsync(DeviceStateInfo? stateInfo = null, CancellationToken ct = default)
        {
            stateInfo ??= LastStateInfo ?? new DeviceStateInfo { State = DeviceProtocolState.Unknown };
            
            var result = new AutoRecoveryResult();
            
            _log?.Invoke($"[自动恢复] 尝试恢复，当前状态: {DeviceStateDetector.GetStateDisplayText(stateInfo.State)}");
            
            try
            {
                switch (stateInfo.State)
                {
                    case DeviceProtocolState.NoResponse:
                        result = await RecoverFromNoResponseAsync(ct);
                        break;
                        
                    case DeviceProtocolState.FirehoseConfigureFailed:
                        result = await RecoverFromConfigureFailedAsync(ct);
                        break;
                        
                    case DeviceProtocolState.PortError:
                        result = await RecoverFromPortErrorAsync(ct);
                        break;
                        
                    case DeviceProtocolState.SaharaWaitingLoader:
                        // Sahara 模式不需要恢复，这是正常状态
                        result.Success = true;
                        result.Message = "设备处于正常的 Sahara 模式";
                        result.NewState = DeviceProtocolState.SaharaWaitingLoader;
                        break;
                        
                    case DeviceProtocolState.FirehoseNotConfigured:
                        result = await RecoverFirehoseNotConfiguredAsync(ct);
                        break;
                        
                    default:
                        result.Success = true;
                        result.Message = "当前状态无需恢复";
                        result.NewState = stateInfo.State;
                        break;
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"恢复失败: {ex.Message}";
            }
            
            _log?.Invoke($"[自动恢复] 结果: {(result.Success ? "成功" : "失败")} - {result.Message}");
            
            return result;
        }

        /// <summary>
        /// 从无响应状态恢复
        /// </summary>
        private async Task<AutoRecoveryResult> RecoverFromNoResponseAsync(CancellationToken ct)
        {
            var result = new AutoRecoveryResult { ActionTaken = "清除缓冲区并重试" };
            
            // 策略 1: 清除缓冲区
            _log?.Invoke("[恢复] 策略1: 清除缓冲区...");
            try
            {
                _port.DiscardInBuffer();
                _port.DiscardOutBuffer();
                await Task.Delay(200, ct);
            }
            catch { }
            
            // 策略 2: 发送 NOP 探测
            _log?.Invoke("[恢复] 策略2: 发送 NOP 探测...");
            try
            {
                string nop = "<?xml version=\"1.0\" ?><data><nop /></data>";
                byte[] nopBytes = System.Text.Encoding.UTF8.GetBytes(nop);
                _port.Write(nopBytes, 0, nopBytes.Length);
                
                await Task.Delay(500, ct);
                
                if (_port.BytesToRead > 0)
                {
                    result.Success = true;
                    result.Message = "设备响应 NOP，恢复成功";
                    result.NewState = DeviceProtocolState.FirehoseNotConfigured;
                    return result;
                }
            }
            catch { }
            
            // 策略 3: 等待 Sahara Hello
            _log?.Invoke("[恢复] 策略3: 等待 Sahara Hello...");
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 3000 && !ct.IsCancellationRequested)
                {
                    if (_port.BytesToRead >= 48)
                    {
                        result.Success = true;
                        result.Message = "检测到 Sahara Hello，设备可能已重启";
                        result.NewState = DeviceProtocolState.SaharaWaitingLoader;
                        return result;
                    }
                    await Task.Delay(100, ct);
                }
            }
            catch { }
            
            result.Success = false;
            result.Message = "无法恢复，请手动重启设备";
            result.NewState = DeviceProtocolState.NoResponse;
            return result;
        }

        /// <summary>
        /// 从 Firehose 配置失败恢复
        /// </summary>
        private async Task<AutoRecoveryResult> RecoverFromConfigureFailedAsync(CancellationToken ct)
        {
            var result = new AutoRecoveryResult { ActionTaken = "重新配置 Firehose" };
            
            // 清除缓冲区
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();
            await Task.Delay(200, ct);
            
            // 尝试不同的配置参数
            var configs = new[]
            {
                ("ufs", 1048576),   // 1MB
                ("ufs", 524288),    // 512KB
                ("emmc", 1048576),  // 1MB eMMC
                ("emmc", 524288),   // 512KB eMMC
            };
            
            foreach (var (storage, maxPayload) in configs)
            {
                _log?.Invoke($"[恢复] 尝试配置: {storage.ToUpper()}, MaxPayload={maxPayload}");
                
                try
                {
                    string configure = $"<?xml version=\"1.0\" ?><data><configure MemoryName=\"{storage}\" " +
                                      $"MaxPayloadSizeToTargetInBytes=\"{maxPayload}\" " +
                                      $"ZlpAwareHost=\"1\" SkipStorageInit=\"0\" /></data>";
                    
                    byte[] configBytes = System.Text.Encoding.UTF8.GetBytes(configure);
                    _port.Write(configBytes, 0, configBytes.Length);
                    
                    await Task.Delay(500, ct);
                    
                    if (_port.BytesToRead > 0)
                    {
                        byte[] response = new byte[_port.BytesToRead];
                        _port.Read(response, 0, response.Length);
                        
                        string responseText = System.Text.Encoding.UTF8.GetString(response);
                        
                        if (responseText.Contains("value=\"ACK\""))
                        {
                            result.Success = true;
                            result.Message = $"配置成功 ({storage.ToUpper()})";
                            result.NewState = DeviceProtocolState.FirehoseConfigured;
                            return result;
                        }
                    }
                }
                catch { }
                
                await Task.Delay(200, ct);
            }
            
            result.Success = false;
            result.Message = "所有配置尝试均失败";
            result.NewState = DeviceProtocolState.FirehoseConfigureFailed;
            return result;
        }

        /// <summary>
        /// 从端口错误恢复
        /// </summary>
        private async Task<AutoRecoveryResult> RecoverFromPortErrorAsync(CancellationToken ct)
        {
            var result = new AutoRecoveryResult { ActionTaken = "重新打开端口" };
            
            try
            {
                // 关闭端口
                if (_port.IsOpen)
                {
                    _port.Close();
                }
                
                await Task.Delay(500, ct);
                
                // 重新打开 (需要端口名，此处无法获取，返回失败)
                result.Success = false;
                result.Message = "需要手动重新打开端口";
                result.NewState = DeviceProtocolState.PortError;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"端口恢复异常: {ex.Message}";
                result.NewState = DeviceProtocolState.PortError;
            }
            
            return result;
        }

        /// <summary>
        /// 从 Firehose 未配置状态恢复 (自动配置)
        /// </summary>
        private async Task<AutoRecoveryResult> RecoverFirehoseNotConfiguredAsync(CancellationToken ct)
        {
            var result = new AutoRecoveryResult { ActionTaken = "自动配置 Firehose" };
            
            // 默认尝试 UFS 配置
            _log?.Invoke("[恢复] 自动配置 Firehose (UFS 1MB)...");
            
            try
            {
                string configure = "<?xml version=\"1.0\" ?><data><configure MemoryName=\"ufs\" " +
                                  "MaxPayloadSizeToTargetInBytes=\"1048576\" " +
                                  "ZlpAwareHost=\"1\" SkipStorageInit=\"0\" /></data>";
                
                byte[] configBytes = System.Text.Encoding.UTF8.GetBytes(configure);
                _port.Write(configBytes, 0, configBytes.Length);
                
                await Task.Delay(500, ct);
                
                if (_port.BytesToRead > 0)
                {
                    byte[] response = new byte[Math.Min(_port.BytesToRead, 4096)];
                    _port.Read(response, 0, response.Length);
                    
                    string responseText = System.Text.Encoding.UTF8.GetString(response);
                    
                    if (responseText.Contains("value=\"ACK\""))
                    {
                        result.Success = true;
                        result.Message = "Firehose 配置成功 (UFS)";
                        result.NewState = DeviceProtocolState.FirehoseConfigured;
                        return result;
                    }
                }
                
                // UFS 失败，尝试 eMMC
                result = await RecoverFromConfigureFailedAsync(ct);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"配置异常: {ex.Message}";
                result.NewState = DeviceProtocolState.FirehoseConfigureFailed;
            }
            
            return result;
        }

        /// <summary>
        /// 手动设置状态 (用于外部更新)
        /// </summary>
        public void SetState(DeviceProtocolState state, string description = "")
        {
            var stateInfo = new DeviceStateInfo
            {
                State = state,
                Description = description,
                CanProceed = state == DeviceProtocolState.SaharaWaitingLoader ||
                            state == DeviceProtocolState.FirehoseConfigured ||
                            state == DeviceProtocolState.FirehoseAuthenticated
            };
            
            UpdateState(stateInfo);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            StopMonitoring();
            _monitorCts?.Dispose();
        }
    }
}

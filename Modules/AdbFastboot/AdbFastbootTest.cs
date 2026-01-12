using System;
using System.Threading;
using System.Threading.Tasks;

namespace tools.Modules.AdbFastboot
{
    /// <summary>
    /// ADB/Fastboot 协议测试类
    /// </summary>
    public static class AdbFastbootTest
    {
        /// <summary>
        /// 运行所有测试
        /// </summary>
        public static async Task RunAllTestsAsync()
        {
            Console.WriteLine("=== ADB/Fastboot 协议测试 ===\n");

            // 1. 测试 ADB Server 连接
            await TestAdbServerAsync();

            // 2. 测试 ADB 功能
            await TestAdbFunctionsAsync();

            // 3. 测试 Fastboot (需要设备在 Fastboot 模式)
            // await TestFastbootAsync();

            Console.WriteLine("\n=== 测试完成 ===");
        }

        /// <summary>
        /// 测试 ADB Server 连接和设备列表
        /// </summary>
        public static async Task TestAdbServerAsync()
        {
            Console.WriteLine("--- 测试 ADB Server ---");

            try
            {
                // 获取设备列表
                var devices = await AdbProtocol.GetDevicesAsync();

                if (devices.Count == 0)
                {
                    Console.WriteLine("⚠ 未检测到设备，请确保：");
                    Console.WriteLine("  1. ADB Server 已运行 (adb start-server)");
                    Console.WriteLine("  2. 设备已连接并授权");
                    Console.WriteLine("  3. USB 调试已启用");
                    return;
                }

                Console.WriteLine($"✓ 检测到 {devices.Count} 个设备:");
                foreach (var (serial, state) in devices)
                {
                    Console.WriteLine($"  - {serial} ({state})");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ ADB Server 连接失败: {ex.Message}");
                Console.WriteLine("  提示: 请先运行 'adb start-server'");
            }
        }

        /// <summary>
        /// 测试 ADB 功能
        /// </summary>
        public static async Task TestAdbFunctionsAsync()
        {
            Console.WriteLine("\n--- 测试 ADB 功能 ---");

            // 先获取设备列表
            var devices = await AdbProtocol.GetDevicesAsync();
            if (devices.Count == 0)
            {
                Console.WriteLine("✗ 没有可用设备");
                return;
            }

            var (serial, state) = devices[0];
            Console.WriteLine($"使用设备: {serial} ({state})");

            var adb = new AdbProtocol();
            adb.OnLog += msg => Console.WriteLine($"  {msg}");

            try
            {
                // 通过 ADB Server 连接
                Console.WriteLine("\n[1] 连接设备...");
                bool connected = await adb.ConnectViaServerAsync(serial);

                if (!connected)
                {
                    Console.WriteLine("✗ 无法连接设备");
                    return;
                }
                Console.WriteLine("✓ 连接成功");

                // 测试 Shell 命令
                Console.WriteLine("\n[2] 测试 Shell 命令...");
                string result = await adb.ShellAsync("echo 'Hello from ADB!'");
                Console.WriteLine($"✓ Shell 响应: {result.Trim()}");

                // 获取设备信息
                Console.WriteLine("\n[3] 获取设备信息...");
                string model = await adb.GetModelAsync();
                string android = await adb.GetAndroidVersionAsync();
                string sdk = await adb.GetSdkVersionAsync();
                Console.WriteLine($"✓ 设备型号: {model}");
                Console.WriteLine($"✓ Android 版本: {android}");
                Console.WriteLine($"✓ SDK 版本: {sdk}");

                // 获取包列表
                Console.WriteLine("\n[4] 获取已安装应用 (前5个)...");
                var packages = await adb.ListPackagesAsync(thirdPartyOnly: true);
                for (int i = 0; i < Math.Min(5, packages.Count); i++)
                {
                    Console.WriteLine($"  - {packages[i]}");
                }
                Console.WriteLine($"✓ 共 {packages.Count} 个第三方应用");

                // 列出目录
                Console.WriteLine("\n[5] 列出 /sdcard 目录...");
                var files = await adb.ListDirAsync("/sdcard");
                Console.WriteLine($"✓ 找到 {files.Count} 个文件/目录");
                for (int i = 0; i < Math.Min(5, files.Count); i++)
                {
                    var (name, mode, size, mtime) = files[i];
                    bool isDir = (mode & 0x4000) != 0;
                    Console.WriteLine($"  {(isDir ? "[D]" : "[F]")} {name}");
                }

                Console.WriteLine("\n✓ ADB 测试完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ 测试失败: {ex.Message}");
            }
            finally
            {
                adb.Disconnect();
                adb.Dispose();
            }
        }

        /// <summary>
        /// 测试 Fastboot 功能 (需要设备在 Fastboot 模式)
        /// </summary>
        public static async Task TestFastbootAsync()
        {
            Console.WriteLine("\n--- 测试 Fastboot 功能 ---");

            var fastboot = new FastbootProtocol();
            fastboot.OnLog += msg => Console.WriteLine($"  {msg}");

            try
            {
                Console.WriteLine("\n[1] 连接 Fastboot 设备...");
                bool connected = fastboot.Connect();

                if (!connected)
                {
                    Console.WriteLine("⚠ 未检测到 Fastboot 设备");
                    Console.WriteLine("  请将设备重启到 Fastboot 模式:");
                    Console.WriteLine("  - adb reboot bootloader");
                    Console.WriteLine("  - 或长按电源+音量下");
                    return;
                }
                Console.WriteLine("✓ Fastboot 连接成功");

                // 获取设备信息
                Console.WriteLine("\n[2] 获取设备信息...");
                fastboot.RefreshDeviceInfo();

                if (fastboot.DeviceInfo != null)
                {
                    Console.WriteLine($"✓ 产品: {fastboot.DeviceInfo.Product}");
                    Console.WriteLine($"✓ 序列号: {fastboot.DeviceInfo.SerialNumber}");
                    Console.WriteLine($"✓ Secure: {fastboot.DeviceInfo.Secure}");
                    Console.WriteLine($"✓ Unlocked: {fastboot.DeviceInfo.Unlocked}");
                    Console.WriteLine($"✓ Fastbootd: {fastboot.DeviceInfo.IsFastbootd}");
                    Console.WriteLine($"✓ 当前槽位: {fastboot.DeviceInfo.CurrentSlot}");
                    Console.WriteLine($"✓ VAB 状态: {fastboot.DeviceInfo.SnapshotUpdateStatus}");
                }

                // 获取分区列表
                Console.WriteLine("\n[3] 获取分区列表...");
                var partitions = fastboot.GetPartitionDetails();
                Console.WriteLine($"✓ 找到 {partitions.Count} 个分区");
                for (int i = 0; i < Math.Min(10, partitions.Count); i++)
                {
                    var p = partitions[i];
                    Console.WriteLine($"  - {p.Name}: {p.SizeFormatted} {(p.IsLogical ? "[逻辑]" : "")}");
                }

                Console.WriteLine("\n✓ Fastboot 测试完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ 测试失败: {ex.Message}");
            }
            finally
            {
                fastboot.Disconnect();
                fastboot.Dispose();
            }
        }

        /// <summary>
        /// 交互式测试
        /// </summary>
        public static async Task InteractiveTestAsync()
        {
            Console.WriteLine("=== ADB 交互式测试 ===");
            Console.WriteLine("输入命令 (输入 'exit' 退出, 'help' 查看帮助)\n");

            var adb = new AdbProtocol();
            adb.OnLog += msg => Console.WriteLine($"[LOG] {msg}");

            bool connected = false;

            while (true)
            {
                Console.Write(connected ? "adb> " : "adb(未连接)> ");
                string? input = Console.ReadLine()?.Trim();

                if (string.IsNullOrEmpty(input))
                    continue;

                if (input == "exit" || input == "quit")
                    break;

                try
                {
                    switch (input.ToLower())
                    {
                        case "help":
                            PrintHelp();
                            break;

                        case "connect":
                            connected = await adb.ConnectViaServerAsync();
                            Console.WriteLine(connected ? "已连接" : "连接失败");
                            break;

                        case "disconnect":
                            adb.Disconnect();
                            connected = false;
                            Console.WriteLine("已断开");
                            break;

                        case "devices":
                            var devices = await AdbProtocol.GetDevicesAsync();
                            foreach (var (serial, state) in devices)
                                Console.WriteLine($"{serial}\t{state}");
                            break;

                        case "info":
                            if (!connected) { Console.WriteLine("请先连接"); break; }
                            Console.WriteLine($"型号: {await adb.GetModelAsync()}");
                            Console.WriteLine($"Android: {await adb.GetAndroidVersionAsync()}");
                            Console.WriteLine($"SDK: {await adb.GetSdkVersionAsync()}");
                            break;

                        case "packages":
                            if (!connected) { Console.WriteLine("请先连接"); break; }
                            var pkgs = await adb.ListPackagesAsync(thirdPartyOnly: true);
                            foreach (var p in pkgs) Console.WriteLine(p);
                            break;

                        case "root":
                            if (!connected) { Console.WriteLine("请先连接"); break; }
                            await adb.RootAsync();
                            break;

                        case "unroot":
                            if (!connected) { Console.WriteLine("请先连接"); break; }
                            await adb.UnrootAsync();
                            break;

                        case "reboot":
                            if (!connected) { Console.WriteLine("请先连接"); break; }
                            await adb.RebootAsync();
                            connected = false;
                            break;

                        case "reboot bootloader":
                            if (!connected) { Console.WriteLine("请先连接"); break; }
                            await adb.RebootBootloaderAsync();
                            connected = false;
                            break;

                        case "reboot recovery":
                            if (!connected) { Console.WriteLine("请先连接"); break; }
                            await adb.RebootRecoveryAsync();
                            connected = false;
                            break;

                        default:
                            if (input.StartsWith("shell "))
                            {
                                if (!connected) { Console.WriteLine("请先连接"); break; }
                                string cmd = input.Substring(6);
                                string result = await adb.ShellAsync(cmd);
                                Console.WriteLine(result);
                            }
                            else if (input.StartsWith("push "))
                            {
                                if (!connected) { Console.WriteLine("请先连接"); break; }
                                var parts = input.Substring(5).Split(' ', 2);
                                if (parts.Length == 2)
                                {
                                    bool ok = await adb.PushAsync(parts[0], parts[1]);
                                    Console.WriteLine(ok ? "成功" : "失败");
                                }
                            }
                            else if (input.StartsWith("pull "))
                            {
                                if (!connected) { Console.WriteLine("请先连接"); break; }
                                var parts = input.Substring(5).Split(' ', 2);
                                if (parts.Length == 2)
                                {
                                    bool ok = await adb.PullAsync(parts[0], parts[1]);
                                    Console.WriteLine(ok ? "成功" : "失败");
                                }
                            }
                            else if (input.StartsWith("install "))
                            {
                                if (!connected) { Console.WriteLine("请先连接"); break; }
                                string apk = input.Substring(8);
                                bool ok = await adb.InstallApkAsync(apk);
                                Console.WriteLine(ok ? "安装成功" : "安装失败");
                            }
                            else
                            {
                                Console.WriteLine("未知命令，输入 'help' 查看帮助");
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"错误: {ex.Message}");
                }
            }

            adb.Dispose();
        }

        private static void PrintHelp()
        {
            Console.WriteLine(@"
可用命令:
  connect              - 连接设备
  disconnect           - 断开连接
  devices              - 列出设备
  info                 - 显示设备信息
  packages             - 列出第三方应用
  root                 - 切换到 root
  unroot               - 取消 root
  reboot               - 重启
  reboot bootloader    - 重启到 bootloader
  reboot recovery      - 重启到 recovery
  shell <command>      - 执行 shell 命令
  push <local> <remote> - 推送文件
  pull <remote> <local> - 拉取文件
  install <apk>        - 安装 APK
  exit                 - 退出
");
        }
    }
}

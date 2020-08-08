using Microsoft.Extensions.Logging.Abstractions;
using SmartGlass;
using SmartGlass.Common;
using SmartGlass.Nano;
using SmartGlass.Nano.Packets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using XboxWebApi.Authentication;
using XboxWebApi.Authentication.Model;
using XboxWebApi.Services;
using XboxWebApi.Services.Api;

namespace xstream
{
    static class Program
    {
        public static string _userHash = null;
        public static string _xToken = null;

        public static AudioFormat _audioFormat = null;
        public static VideoFormat _videoFormat = null;
        public static AudioFormat _chatAudioFormat = null;

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            AllocConsole();

            Console.Write("tokenFilePath: ");
            string tokenFilePath = Console.ReadLine();

            AuthenticationService auth;

            if (!File.Exists(tokenFilePath))
            {
                Shell.WriteLine("Warning: '{0}' file not found.\n", tokenFilePath);

                string reqURL = AuthenticationService.GetWindowsLiveAuthenticationUrl();

                Console.WriteLine("1) Open following URL in your WebBrowser:\n\n{0}\n\n" +
                                        "2) Authenticate with your Microsoft Account\n" +
                                        "3) Paste returned URL from addressbar: \n", reqURL);

                // Call requestUrl via WebWidget or manually and authenticate

                try
                {
                    string url = Console.ReadLine();
                    WindowsLiveResponse rep = AuthenticationService.ParseWindowsLiveResponse(url);
                    auth = new AuthenticationService(rep);

                    auth.Authenticate();
                }
                catch (Exception e)
                {
                    Shell.WriteLine($"Error: Authentication failed, error: {e.Message}");
                    Shell.PressAnyKeyToContinue();
                    return;
                }

                Console.WriteLine(auth.XToken);
                Console.WriteLine(auth.UserInformation);

                // Save token to JSON

                FileStream tokenOutputFile = null;
                try
                {
                    tokenOutputFile = new FileStream(tokenFilePath, FileMode.Create);
                }
                catch (Exception e)
                {
                    Shell.WriteLine("Error: Failed to open token outputfile \'{0}\', error: {1}",
                        tokenOutputFile, e.Message);
                    Shell.PressAnyKeyToContinue();
                    return;
                }
                auth.DumpToFile(tokenOutputFile);
                tokenOutputFile.Close();

                Console.WriteLine("Storing tokens to file \'{0}\' on successful auth",
                        tokenOutputFile.Name);
            }
            else
            {
                // Load token from JSON

                FileStream fs = new FileStream(tokenFilePath, FileMode.Open);
                auth = AuthenticationService.LoadFromFile(fs);
                try
                {
                    auth.Authenticate();
                }
                catch (Exception e)
                {
                    Shell.WriteLine($"Error: Failed to refresh XBL tokens, error: {e.Message}");
                    Shell.PressAnyKeyToContinue();
                    return;
                }
                fs.Close();
            }

            _userHash = auth.XToken.UserInformation.Userhash;
            _xToken = auth.XToken.Jwt;

            Discover().Wait();

            Console.Write("Input IP Address or hostname: ");
            string addressOrHostname = Console.ReadLine();

            Console.WriteLine($"Connecting to {addressOrHostname}...");
            SmartGlassClient client;
            try
            {
                Task<SmartGlassClient> connect = SmartGlassClient.ConnectAsync(
                        addressOrHostname, _userHash, _xToken);

                // ���Taskʧ����GetAwaiter()��ֱ���׳��쳣����Task.Wait()���׳�AggregateException
                client = connect.GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                if (e is SmartGlassException)
                    Shell.WriteLine($"Error: Failed to connect: {e.Message}");
                else if (e is TimeoutException)
                    Shell.WriteLine($"Error: Timeout while connecting: {e.Message}");
                else
                    Shell.WriteLine($"Error: {e}");

                Shell.PressAnyKeyToContinue();
                return;
            }

            // Get general gamestream configuration
            GamestreamConfiguration config = GamestreamConfiguration.GetStandardConfig();
            // Modify standard config, if desired

            GamestreamSession session = client.BroadcastChannel.StartGamestreamAsync(config)
                .GetAwaiter().GetResult();
            Console.WriteLine($"Connecting to NANO // TCP: {session.TcpPort}, UDP: {session.UdpPort}");

            Console.WriteLine($"Running protocol init...");
            NanoClient nano = new NanoClient(addressOrHostname, session);
            try
            {
                // General Handshaking & Opening channels
                nano.InitializeProtocolAsync().Wait();

                // Start Controller input channel
                nano.OpenInputChannelAsync(1280, 720).Wait();

                //IConsumer consumer = /* initialize consumer */;
                //nano.AddConsumer(consumer);

                // Start consumer, if necessary
                //consumer.Start();

                // Audio & Video client handshaking
                // Sets desired AV formats
                Console.WriteLine("Initializing AV stream (handshaking)...");

                _audioFormat = nano.AudioFormats[0];
                _videoFormat = nano.VideoFormats[0];

                nano.InitializeStreamAsync(_audioFormat, _videoFormat).Wait();

                // Start ChatAudio channel
                // TODO: Send opus audio chat samples to console
                _chatAudioFormat = new AudioFormat(1, 24000, AudioCodec.Opus);
                nano.OpenChatAudioChannelAsync(_chatAudioFormat).Wait();

                // Tell console to start sending AV frames
                Console.WriteLine("Starting stream...");

                nano.StartStreamAsync().Wait();

                Shell.WriteLine("Note: Stream is running");
            }
            catch (Exception e)
            {
                Shell.WriteLine($"Error: Failed to init Nano, error: {e}");
                Shell.PressAnyKeyToContinue();
                return;
            }

            // Run a mainloop, to gather controller input events or similar

            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Xstream());

            // finally (dirty)
            Process.GetCurrentProcess().Kill();
        }

        async static Task Discover()
        {
            Console.WriteLine("{0,-15} {1,-36} {2,-15} {3,-16}", "Name", "HardwareId", "Address", "LiveId");

            IEnumerable<Device> devices = await Device.DiscoverAsync();
            foreach (Device device in devices)
            {
                Console.WriteLine("{0,-15} {1,-36} {2,-15} {3,-16}",
                    device.Name, device.HardwareId, device.Address, device.LiveId);
            }
        }

        [DllImport("kernel32")]
        public static extern bool AllocConsole();
        [DllImport("kernel32")]
        public static extern bool FreeConsole();
    }
}

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Windows;
using vJoyInterfaceWrap;
using System.Windows.Media;
using System.Windows.Documents;
using System.Windows.Controls;

/*
 * TODO:
 * 
 * Now:
 * - Settings
 * - Port configuration
 * 
 * Later:
 * - Add multiplayer
 */

namespace Sim_Companion {

    public partial class MainWindow : Window {
        private const byte VERSION_CODE = 0;

        private readonly int DATA_PORT = 8246;
        private readonly int DISCOVERY_PORT = 9246;

        private const int ANNOUNCE = 100;

        private readonly Thread InputThread = null;
        private UdpClient client = null;

        private readonly Thread DiscoveryThread = null;
        private UdpClient discoveryClient = null;

        private bool updateAvailable = false;

        private readonly vJoy joystick = new vJoy();
        private byte id = 0;


        private readonly System.Windows.Forms.NotifyIcon ni;

        public MainWindow() {
            InitializeComponent();

            ni = new System.Windows.Forms.NotifyIcon {
                Icon = new System.Drawing.Icon(Application.GetResourceStream(new Uri("/sc_icon.ico", UriKind.Relative)).Stream),
                Visible = false,
                Text = "Sim Companion",
                BalloonTipText = "Minimized to tray"
            };
            ni.ContextMenu = new System.Windows.Forms.ContextMenu(new System.Windows.Forms.MenuItem[] {
                new System.Windows.Forms.MenuItem("Show", delegate (object sender, EventArgs args) {
                    ni.Visible = false;
                    Show();
                    WindowState = WindowState.Normal;
                }),
                new System.Windows.Forms.MenuItem("Quit", delegate (object sender, EventArgs args) {
                    //Application.Current.Shutdown();
                    Close();
                })
            });

            versionText.Text = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString() + " (alpha)";

            /*var parser = new FileIniDataParser();

            if(!File.Exists("Config.ini")) {
                IniData data = new IniData();
                data["Network"]["data_port"] = DATA_PORT.ToString();
                data["Network"]["discovery_port"] = DISCOVERY_PORT.ToString();
                parser.WriteFile(".\\Config.ini", data, null);
            }


            try {
                IniData data = parser.ReadFile("Config.ini");

                try {
                    string dataPort = data["Network"]["data_port"];
                    DATA_PORT = int.Parse(dataPort);
                }
                catch (Exception) {
                    data["Network"]["data_port"] = DATA_PORT.ToString();
                    parser.WriteFile(".\\Config.ini", data, null);
                }

                try {
                    string discoveryPort = data["Network"]["discovery_port"];
                    DISCOVERY_PORT = int.Parse(discoveryPort);
                }
                catch (Exception) {
                    data["Network"]["discovery_port"] = DISCOVERY_PORT.ToString();
                    parser.WriteFile(".\\Config.ini", data, null);
                }
            }
            catch {
                // Should not happen
                IniData data = new IniData();
                data["Network"]["data_port"] = DATA_PORT.ToString();
                data["Network"]["discovery_port"] = DISCOVERY_PORT.ToString();
                parser.WriteFile(".\\Config.ini", data, null);
            }*/


            joystick.RegisterRemovalCB(vJoyConfigChanged, null);

            if (!joystick.vJoyEnabled()) {
                tabControl.SelectedIndex = 1;
                return;
            }


            for (byte i = 1; i <= 16; i++) {
                if (AcquireDevice(i) < 2 && id == 0) {
                    id = i;
                    break;
                }
            }

            if (id == 0) {
                return;
            }

            // Register the callback function & pass the dialog box object
            //joystick.FfbRegisterGenCB(FfbCB, null);



            InputThread = new Thread(new ThreadStart(GamePad)) {
                IsBackground = true
            };
            InputThread.Start();

            DiscoveryThread = new Thread(new ThreadStart(Discovery)) {
                IsBackground = true
            };
            DiscoveryThread.Start();
        }

        /*private void FfbCB(IntPtr data, object userData) {
            FFBPType type = new FFBPType();
            if(joystick.Ffb_h_Type(data, ref type) == 0) {
                Debug.WriteLine(type);
            }
            //Debug.WriteLine(userData);
        }*/

        private byte AcquireDevice(byte _id) {
            VjdStat status = joystick.GetVJDStatus(_id);
            switch (status) {
                case VjdStat.VJD_STAT_OWN:
                    //Debug.WriteLine("vJoy Device {0} is already owned by this feeder\n", _id);
                    joystick.ResetVJD(_id);// Reset this device to default values
                    return 0;
                case VjdStat.VJD_STAT_FREE:
                    //Debug.WriteLine("vJoy Device {0} is free\n", _id);
                    if (joystick.AcquireVJD(_id)) {
                        //Debug.WriteLine(string.Format("Acquired: vJoy device number {0}.", _id));
                        joystick.ResetVJD(_id);
                        return 1;
                    }
                    //Debug.WriteLine(string.Format("Failed to acquire vJoy device number {0}.", _id));
                    return 5;
                case VjdStat.VJD_STAT_BUSY:
                    //Debug.WriteLine("vJoy Device {0} is already owned by another feeder\nCannot continue\n", _id);
                    return 2;
                case VjdStat.VJD_STAT_MISS:
                    //Debug.WriteLine("vJoy Device {0} is not installed or disabled\nCannot continue\n", _id);
                    return 3;
                default:
                    //Debug.WriteLine("vJoy Device {0} general error\nCannot continue\n", _id);
                    return 4;
            };
        }


        private void Discovery() {
            discoveryClient = new UdpClient();
            discoveryClient.Client.Bind(new IPEndPoint(IPAddress.Any, DISCOVERY_PORT));

            /**
             *  0 - Server/Client
             *  1 - Status (100 = Announce)
             *  2 - Compatibility code
             *  3 - ID(Requested/Reserved) or 0 for auto-assign (Future use)
             */
            try {
                IPEndPoint from = new IPEndPoint(0, 0);
                discoveryClient.Send(new byte[] { 200, ANNOUNCE, VERSION_CODE, 0 }, 4, "255.255.255.255", DISCOVERY_PORT);
                byte[] data = { 200, 0, VERSION_CODE, 0 };
                while (true) {
                    byte[] buff = discoveryClient.Receive(ref from);

                    if (buff.Length < 4 || buff[0] == 200) {
                        continue;
                    }

                    if(buff[1] == ANNOUNCE) {
                        discoveryClient.Send(data, data.Length, from);
                    }

                    if (buff[2] > VERSION_CODE) {
                        updateAvailable = true;
                        homeText.Dispatcher.Invoke(PrintHomeText, System.Windows.Threading.DispatcherPriority.Normal);
                    }
                }
            }
            catch {
                Debug.WriteLine("Discovery loop exit");
            }
        }


        private void GamePad() {
            try {
                client = new UdpClient(DATA_PORT);
                IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, DATA_PORT);

                vJoy.JoystickState joystickState = new vJoy.JoystickState();
                joystickState.bDevice = id;

                while (true) {
                    byte[] data = client.Receive(ref endPoint);
                    // TODO: Remove?
                    /*if(data.Length < 32) {
                        continue;
                    }*/

                    joystickState.AxisZRot = BitConverter.ToUInt16(data, 0);
                    joystickState.AxisXRot = BitConverter.ToUInt16(data, 2);
                    joystickState.AxisYRot = BitConverter.ToUInt16(data, 4);

                    joystickState.AxisZ = BitConverter.ToUInt16(data, 6);
                    joystickState.AxisX = BitConverter.ToUInt16(data, 8);
                    joystickState.AxisY = BitConverter.ToUInt16(data, 10);

                    joystickState.Slider = BitConverter.ToUInt16(data, 12);
                    joystickState.Dial = BitConverter.ToUInt16(data, 14);

                    joystickState.Buttons = BitConverter.ToUInt32(data, 16);

                    joystickState.ButtonsEx1 = BitConverter.ToUInt32(data, 20);

                    joystickState.ButtonsEx2 = BitConverter.ToUInt32(data, 24);

                    joystickState.ButtonsEx3 = BitConverter.ToUInt32(data, 28);

                    joystick.UpdateVJD(id, ref joystickState);
                }
            }
            catch {
                Debug.WriteLine("Receive loop exit");
            }

            joystick.RelinquishVJD(id);
        }

        private void PrintHomeText() {
            homeText.Inlines.Clear();
            
            if(updateAvailable) {
                homeText.Inlines.Add(new Bold(new Run("Update available") { Foreground = Brushes.Yellow }));
            }

            /*if (!joystick.vJoyEnabled()) {
                homeText.Inlines.Add(new Bold(new Run("vJoy driver not found!") { Foreground = Brushes.Red }));
                homeText.Inlines.Add(new Run("\nPlease download and install vJoy driver from: "));
                Hyperlink driverLink = new Hyperlink(new Run("https://sourceforge.net/projects/vjoystick/files/latest/download")) { NavigateUri = new Uri("https://sourceforge.net/projects/vjoystick/files/latest/download") };
                driverLink.RequestNavigate += new System.Windows.Navigation.RequestNavigateEventHandler(delegate (object sender, System.Windows.Navigation.RequestNavigateEventArgs e) {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://sourceforge.net/projects/vjoystick/files/latest/download") {
                        UseShellExecute = true,
                    });
                });
                homeText.Inlines.Add(driverLink);
            }*/
        }

        private void PrintInfo() {
            infoText.Inlines.Clear();

            infoText.Inlines.Add(new Bold(new Run("Ports:")));
            infoText.Inlines.Add(new Run("\nInput:  " + DATA_PORT));
            infoText.Inlines.Add(new Run("\nDiscovery:  " + DISCOVERY_PORT + "\n\n"));

            if (!joystick.vJoyEnabled()) {
                infoText.Inlines.Add(new Bold(new Run("vJoy driver not found!") { Foreground = Brushes.Red }));
                infoText.Inlines.Add(new Run("\nPlease download and install vJoy driver from: "));
                Hyperlink driverLink = new Hyperlink(new Run("https://sourceforge.net/projects/vjoystick/files/latest/download")) { NavigateUri = new Uri("https://sourceforge.net/projects/vjoystick/files/latest/download") };
                driverLink.RequestNavigate += new System.Windows.Navigation.RequestNavigateEventHandler(delegate (object sender, System.Windows.Navigation.RequestNavigateEventArgs e) {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://sourceforge.net/projects/vjoystick/files/latest/download") {
                        UseShellExecute = true,
                    });
                });
                infoText.Inlines.Add(driverLink);
                return;
            }

            infoText.Inlines.Add(new Bold(new Run("\n\nvJoy Driver:")));
            infoText.Inlines.Add("\nVendor:  " + joystick.GetvJoyManufacturerString() + "\nProduct:  " + joystick.GetvJoyProductString() + "\nVersion:  " + joystick.GetvJoySerialNumberString());

            infoText.Inlines.Add(new Bold(new Run("\n\nDevices:")));
            for (byte i = 1; i <= 16; i++) {
                if (AcquireDevice(i) < 2) {
                    if (i == id) {
                        infoText.Inlines.Add(new Bold(new Run("\n" + i) { Foreground = Brushes.Green }));
                        infoText.Inlines.Add(new Run(" (active)"));
                        infoText.Inlines.Add(new Run("\n   ├─ Axis:"));
                        infoText.Inlines.Add(new Run("\n   │      X:  "));
                        if (joystick.GetVJDAxisExist(id, HID_USAGES.HID_USAGE_X)) {
                            infoText.Inlines.Add(new Bold(new Run("OK") { Foreground = Brushes.Green }));
                        }
                        else {
                            infoText.Inlines.Add(new Bold(new Run("NOT CONFIGURED") { Foreground = Brushes.Red }));
                        }
                        infoText.Inlines.Add(new Run("\n   │      Y:  "));
                        if (joystick.GetVJDAxisExist(id, HID_USAGES.HID_USAGE_Y)) {
                            infoText.Inlines.Add(new Bold(new Run("OK") { Foreground = Brushes.Green }));
                        }
                        else {
                            infoText.Inlines.Add(new Bold(new Run("NOT CONFIGURED") { Foreground = Brushes.Red }));
                        }
                        infoText.Inlines.Add(new Run("\n   │      Z:  "));
                        if (joystick.GetVJDAxisExist(id, HID_USAGES.HID_USAGE_Z)) {
                            infoText.Inlines.Add(new Bold(new Run("OK") { Foreground = Brushes.Green }));
                        }
                        else {
                            infoText.Inlines.Add(new Bold(new Run("NOT CONFIGURED") { Foreground = Brushes.Red }));
                        }
                        infoText.Inlines.Add(new Run("\n   │      RX:  "));
                        if (joystick.GetVJDAxisExist(id, HID_USAGES.HID_USAGE_RX)) {
                            infoText.Inlines.Add(new Bold(new Run("OK") { Foreground = Brushes.Green }));
                        }
                        else {
                            infoText.Inlines.Add(new Bold(new Run("NOT CONFIGURED") { Foreground = Brushes.Red }));
                        }
                        infoText.Inlines.Add(new Run("\n   │      RY:  "));
                        if (joystick.GetVJDAxisExist(id, HID_USAGES.HID_USAGE_RY)) {
                            infoText.Inlines.Add(new Bold(new Run("OK") { Foreground = Brushes.Green }));
                        }
                        else {
                            infoText.Inlines.Add(new Bold(new Run("NOT CONFIGURED") { Foreground = Brushes.Red }));
                        }
                        infoText.Inlines.Add(new Run("\n   │      RZ:  "));
                        if (joystick.GetVJDAxisExist(id, HID_USAGES.HID_USAGE_RZ)) {
                            infoText.Inlines.Add(new Bold(new Run("OK") { Foreground = Brushes.Green }));
                        }
                        else {
                            infoText.Inlines.Add(new Bold(new Run("NOT CONFIGURED") { Foreground = Brushes.Red }));
                        }

                        infoText.Inlines.Add(new Run("\n   │\n   ├─ Sliders:"));
                        infoText.Inlines.Add(new Run("\n   │      Slider 1:  "));
                        if (joystick.GetVJDAxisExist(id, HID_USAGES.HID_USAGE_SL0)) {
                            infoText.Inlines.Add(new Bold(new Run("OK") { Foreground = Brushes.Green }));
                        }
                        else {
                            infoText.Inlines.Add(new Bold(new Run("NOT CONFIGURED") { Foreground = Brushes.Red }));
                        }
                        infoText.Inlines.Add(new Run("\n   │      Slider 2:  "));
                        if (joystick.GetVJDAxisExist(id, HID_USAGES.HID_USAGE_SL1)) {
                            infoText.Inlines.Add(new Bold(new Run("OK") { Foreground = Brushes.Green }));
                        }
                        else {
                            infoText.Inlines.Add(new Bold(new Run("NOT CONFIGURED") { Foreground = Brushes.Red }));
                        }

                        infoText.Inlines.Add(new Run("\n   │\n   └─ Buttons:  "));
                        int nBtn = joystick.GetVJDButtonNumber(id);
                        if (nBtn >= 128) {
                            infoText.Inlines.Add(new Bold(new Run(nBtn + "") { Foreground = Brushes.Green }));
                        }
                        else {
                            infoText.Inlines.Add(new Bold(new Run(nBtn + " (Configure vJoy to 128 buttons)") { Foreground = Brushes.Red }));
                        }
                    }
                    else {
                        infoText.Inlines.Add(new Run("\n" + i) { Foreground = Brushes.Green });
                    }
                }
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
            if (client != null) {
                client.Close();
            }
            if (discoveryClient != null) {
                discoveryClient.Close();
            }
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (((TabControl)sender).SelectedIndex == 0) {
                if (homeText.Inlines.Count == 0) {
                    PrintHomeText();
                }
            }
            else if (((TabControl) sender).SelectedIndex == 1 && infoText.Inlines.Count == 0) {
                PrintInfo();
            }
        }

        void vJoyConfigChanged(bool removed, bool first, object userData) {
            if (!first) {
                if(!removed) {
                    for (byte i = 1; i <= 16; i++) {
                        if (AcquireDevice(i) < 2 && id == 0) {
                            id = i;
                            break;
                        }
                    }
                }

                infoText.Dispatcher.Invoke(PrintInfo, System.Windows.Threading.DispatcherPriority.Normal);
            }
        }

        private void Window_StateChanged(object sender, EventArgs e) {
            //if(WindowState == WindowState.Minimized && mttToggle.IsOn) {
            if(WindowState == WindowState.Minimized) {
                Hide();
                ni.Visible = true;
                ni.ShowBalloonTip(2000);
            }
        }

        public void BringToFront() {
            if(WindowState == WindowState.Minimized || Visibility == Visibility.Hidden) {
                if(ni.Visible) {
                    ni.Visible = false;
                }

                Show();
                WindowState = WindowState.Normal;
            }

            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }
    }
}

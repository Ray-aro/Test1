using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Threading;
using OxyPlot;
using OxyPlot.Series;
using OxyPlot.Axes;
using OxyPlot.Annotations;
using System.Configuration;

namespace Measurement_Code
{
    public partial class Form1 : Form
    {
        public static Form1 form1;
        /* Global variables */
        public const int MAX_INTERFACE_COUNT = 5;
        public const int MAX_RESOULUTIONS = 6;

        static public uint uiResolution = 0;
        static public uint hLLT = 0;
        static public CLLTI.TScannerType tscanCONTROLType;

        static byte[] abyProfileBuffer;

        static uint uiReceivedProfileCount = 0;
        static uint uiNeededProfileCount = 1; // Profile count until event is set
        static uint uiProfileDataSize = 0;
        static uint uiExposureTime = 100;
        static double uiExposureTimeTmp= uiExposureTime/100;
        static uint uiIdleTime = 0;

        // Define an array with two AutoResetEvent WaitHandles. 
        static AutoResetEvent hProfileEvent = new AutoResetEvent(false);

        Thread calThread;
        public delegate void calDelegate(string tmp);
        static public calDelegate calDelegate1;
        static int iRetValue;
        static bool bOK = true;
        static bool bConnected = false;
        static bool IsOver = false;
        static bool IsClosed = false;
        static List<double[]> dataSaveX;
        static List<double[]> dataSaveZ;

        static PlotModel myModel;
        static bool PlotMouseDown = false;
        static bool StartROISet = false;
        static bool ROIChanged = false;
        static int ROIState = 0;
        static double[] ROIX= {-100000,100000};
        static LineSeries ROI1 = new LineSeries();
        static LineSeries ROI2 = new LineSeries();
        static LineSeries myLine = new LineSeries();
        static LineSeries myLine1 = new LineSeries();
        static LinearAxis yValueAxis = new LinearAxis();
        static LinearAxis xValueAxis = new LinearAxis();

        public Form1()
        {
            InitializeComponent();
            form1 = this;
            
        }


        static unsafe void scanCONTROL_Sample()
        {
            uint[] auiInterfaces = new uint[MAX_INTERFACE_COUNT];
            uint[] auiResolutions = new uint[MAX_RESOULUTIONS];

            StringBuilder sbDevName = new StringBuilder(100);
            StringBuilder sbVenName = new StringBuilder(100);

            CLLTI.ProfileReceiveMethod fnProfileReceiveMethod = null;

            uint uiBufferCount = 20, uiMainReflection = 0, uiPacketSize = 1024;

            int iInterfaceCount = 0;


            dataSaveX = new List<double[]>();
            dataSaveZ = new List<double[]>();

            hLLT = 0;
            uiResolution = 0;

            ReadLog("----- Connect to scanCONTROL -----\n");

            //Create a Ethernet Device -> returns handle to LLT device
            hLLT = CLLTI.CreateLLTDevice(CLLTI.TInterfaceType.INTF_TYPE_ETHERNET);
            if (hLLT != 0)
                ReadLog("CreateLLTDevice OK");
            else
                ReadLog("Error during CreateLLTDevice\n");

            //Gets the available interfaces from the scanCONTROL-device
            iInterfaceCount = CLLTI.GetDeviceInterfacesFast(hLLT, auiInterfaces, auiInterfaces.GetLength(0));
            if (iInterfaceCount <= 0)
                ReadLog("FAST: There is no scanCONTROL connected");
            else if (iInterfaceCount == 1)
                ReadLog("FAST: There is 1 scanCONTROL connected ");
            else
                ReadLog("FAST: There are " + iInterfaceCount + " scanCONTROL's connected");

            if (iInterfaceCount >= 1)
            {
                uint target4 = auiInterfaces[0] & 0x000000FF;
                uint target3 = (auiInterfaces[0] & 0x0000FF00) >> 8;
                uint target2 = (auiInterfaces[0] & 0x00FF0000) >> 16;
                uint target1 = (auiInterfaces[0] & 0xFF000000) >> 24;

                // Set the first IP address detected by GetDeviceInterfacesFast to handle
                ReadLog("Select the device interface: " + target1 + "." + target2 + "." + target3 + "." + target4);
                if ((iRetValue = CLLTI.SetDeviceInterface(hLLT, auiInterfaces[0], 0)) < CLLTI.GENERAL_FUNCTION_OK)
                {
                    OnError("Error during SetDeviceInterface", iRetValue);
                    bOK = false;
                }

                if (bOK)
                {
                    // Connect to sensor with the device interface set before
                    ReadLog("Connecting to scanCONTROL");
                    if ((iRetValue = CLLTI.Connect(hLLT)) < CLLTI.GENERAL_FUNCTION_OK)
                    {
                        OnError("Error during Connect", iRetValue);
                        bOK = false;
                    }
                    else
                        bConnected = true;
                }

                if (bOK)
                {
                    ReadLog("\n----- Get scanCONTROL Info -----\n");

                    // Read the device name and vendor from scanner
                    ReadLog("Get Device Name");
                    if ((iRetValue = CLLTI.GetDeviceName(hLLT, sbDevName, sbDevName.Capacity, sbVenName, sbVenName.Capacity)) < CLLTI.GENERAL_FUNCTION_OK)
                    {
                        OnError("Error during GetDevName", iRetValue);
                        bOK = false;
                    }
                    else
                    {
                        ReadLog(" - Devname: " + sbDevName + "\n - Venname: " + sbVenName);
                    }
                }

                if (bOK)
                {
                    // Get the scanCONTROL type and check if it is valid
                    ReadLog("Get scanCONTROL type");
                    if ((iRetValue = CLLTI.GetLLTType(hLLT, ref tscanCONTROLType)) < CLLTI.GENERAL_FUNCTION_OK)
                    {
                        OnError("Error during GetLLTType", iRetValue);
                        bOK = false;
                    }

                    if (iRetValue == CLLTI.GENERAL_FUNCTION_DEVICE_NAME_NOT_SUPPORTED)
                    {
                        ReadLog(" - Can't decode scanCONTROL type. Please contact Micro-Epsilon for a newer version of the LLT.dll.");
                    }

                    if (tscanCONTROLType >= CLLTI.TScannerType.scanCONTROL27xx_25 && tscanCONTROLType <= CLLTI.TScannerType.scanCONTROL27xx_xxx)
                    {
                        ReadLog(" - The scanCONTROL is a scanCONTROL27xx");
                    }
                    else if (tscanCONTROLType >= CLLTI.TScannerType.scanCONTROL25xx_25 && tscanCONTROLType <= CLLTI.TScannerType.scanCONTROL25xx_xxx)
                    {
                        ReadLog(" - The scanCONTROL is a scanCONTROL25xx");
                    }
                    else if (tscanCONTROLType >= CLLTI.TScannerType.scanCONTROL26xx_25 && tscanCONTROLType <= CLLTI.TScannerType.scanCONTROL26xx_xxx)
                    {
                        ReadLog(" - The scanCONTROL is a scanCONTROL26xx");
                    }
                    else if (tscanCONTROLType >= CLLTI.TScannerType.scanCONTROL29xx_25 && tscanCONTROLType <= CLLTI.TScannerType.scanCONTROL29xx_xxx)
                    {
                        ReadLog(" - The scanCONTROL is a scanCONTROL29xx");
                    }
                    else if (tscanCONTROLType >= CLLTI.TScannerType.scanCONTROL30xx_25 && tscanCONTROLType <= CLLTI.TScannerType.scanCONTROL30xx_xxx)
                    {
                        ReadLog(" - The scanCONTROL is a scanCONTROL30xx");
                    }
                    else
                    {
                        ReadLog(" - The scanCONTROL is a undefined type\nPlease contact Micro-Epsilon for a newer SDK");
                    }

                    // Get all possible resolutions for connected sensor and save them in array 
                    ReadLog("Get all possible resolutions");
                    if ((iRetValue = CLLTI.GetResolutions(hLLT, auiResolutions, auiResolutions.GetLength(0))) < CLLTI.GENERAL_FUNCTION_OK)
                    {
                        OnError("Error during GetResolutions", iRetValue);
                        bOK = false;
                    }

                    // Set the max. possible resolution
                    uiResolution = auiResolutions[0];
                }

                // Set scanner settings to valid parameters for this example                

                if (bOK)
                {
                    ReadLog("\n----- Set scanCONTROL Parameters -----\n");

                    ReadLog("Set resolution to " + uiResolution);
                    if ((iRetValue = CLLTI.SetResolution(hLLT, uiResolution)) < CLLTI.GENERAL_FUNCTION_OK)
                    {
                        OnError("Error during SetResolution", iRetValue);
                        bOK = false;
                    }
                }

                if (bOK)
                {
                    ReadLog("Set BufferCount to " + uiBufferCount);
                    if ((iRetValue = CLLTI.SetBufferCount(hLLT, uiBufferCount)) < CLLTI.GENERAL_FUNCTION_OK)
                    {
                        OnError("Error during SetBufferCount", iRetValue);
                        bOK = false;
                    }
                }

                if (bOK)
                {
                    ReadLog("Set MainReflection to " + uiMainReflection);
                    if ((iRetValue = CLLTI.SetMainReflection(hLLT, uiMainReflection)) < CLLTI.GENERAL_FUNCTION_OK)
                    {
                        OnError("Error during SetMainReflection", iRetValue);
                        bOK = false;
                    }
                }

                if (bOK)
                {
                    ReadLog("Set Packetsize to " + uiPacketSize);
                    if ((iRetValue = CLLTI.SetPacketSize(hLLT, uiPacketSize)) < CLLTI.GENERAL_FUNCTION_OK)
                    {
                        OnError("Error during SetPacketSize", iRetValue);
                        bOK = false;
                    }
                }

                if (bOK)
                {
                    ReadLog("Set Profile config to PROFILE");
                    if ((iRetValue = CLLTI.SetProfileConfig(hLLT, CLLTI.TProfileConfig.PROFILE)) < CLLTI.GENERAL_FUNCTION_OK)
                    {
                        OnError("Error during SetProfileConfig", iRetValue);
                        bOK = false;
                    }
                }
                if (bOK)
                {
                    if ((iRetValue = CLLTI.SetFeature(hLLT, CLLTI.FEATURE_FUNCTION_TRIGGER, CLLTI.TRIG_INTERNAL)) < CLLTI.GENERAL_FUNCTION_OK)
                    {
                        OnError("Error during SetFeature(FEATURE_FUNCTION_TRIGGER)", iRetValue);
                        bOK = false;
                    }
                }
                if (bOK)
                {
                    ReadLog("Set trigger to pos. edge mode");
                    uint Trigger = CLLTI.TRIG_MODE_EDGE | CLLTI.TRIG_POLARITY_HIGH;
                    // Set digital input as trigger input and activate ext. triggering
                    Trigger |= CLLTI.TRIG_INPUT_DIGIN | CLLTI.TRIG_EXT_ACTIVE;
                    // Set trigger settings
                    if ((iRetValue = CLLTI.SetFeature(hLLT, CLLTI.FEATURE_FUNCTION_TRIGGER, Trigger)) < CLLTI.GENERAL_FUNCTION_OK)
                    {

                        OnError("Error during SetFeature(FEATURE_FUNCTION_TRIGGER)", iRetValue);
                        bOK = false;

                    }

                    //ReadLog("Set trigger to internal");
                    //if ((iRetValue = CLLTI.SetFeature(hLLT, CLLTI.FEATURE_FUNCTION_TRIGGER, CLLTI.TRIG_INTERNAL)) < CLLTI.GENERAL_FUNCTION_OK)
                    //{
                    //    OnError("Error during SetFeature(FEATURE_FUNCTION_TRIGGER)", iRetValue);
                    //    bOK = false;
                    //}
                }

                if (bOK)
                {
                    ReadLog("Set exposure time to " + uiExposureTimeTmp+"ms");
                    if ((iRetValue = CLLTI.SetFeature(hLLT, CLLTI.FEATURE_FUNCTION_EXPOSURE_TIME, uiExposureTime)) < CLLTI.GENERAL_FUNCTION_OK)
                    {
                        OnError("Error during SetFeature(FEATURE_FUNCTION_EXPOSURE_TIME)", iRetValue);
                        bOK = false;
                    }
                }

                if (bOK)
                {
                    //ReadLog("Set idle time to " + uiIdleTime / 100 + "ms");
                    if ((iRetValue = CLLTI.SetFeature(hLLT, CLLTI.FEATURE_FUNCTION_IDLE_TIME, uiIdleTime)) < CLLTI.GENERAL_FUNCTION_OK)
                    {
                        OnError("Error during SetFeature(FEATURE_FUNCTION_IDLE_TIME)", iRetValue);
                        bOK = false;
                    }
                }
                if (bOK)
                {
                    Console.WriteLine("Set Profile config to PROFILE");
                    if ((iRetValue = CLLTI.SetProfileConfig(hLLT, CLLTI.TProfileConfig.PROFILE)) < CLLTI.GENERAL_FUNCTION_OK)
                    {
                        OnError("Error during SetProfileConfig", iRetValue);
                        bOK = false;
                    }
                }

                // Setup callback
                if (bOK)
                {
                    ReadLog("\n----- Setup Callback function and event -----\n");

                    ReadLog("Register the callback");
                    // Set the callback function
                    fnProfileReceiveMethod = new CLLTI.ProfileReceiveMethod(ProfileEvent);

                    // Register the callback
                    if ((iRetValue = CLLTI.RegisterCallback(hLLT, CLLTI.TCallbackType.STD_CALL, fnProfileReceiveMethod, hLLT)) < CLLTI.GENERAL_FUNCTION_OK)
                    {
                        OnError("Error during RegisterCallback", iRetValue);
                        bOK = false;
                    }
                }

                // Main tasks in this example
                if (bOK)
                {
                    ReadLog("\n----- Get profiles with Callback from scanCONTROL -----\n");

                    GetProfiles_Callback();

                    
                }
            }
        }

        /*
         * Evalute reveived profiles via callback function
         */
        static void GetProfiles_Callback()
        {
            IsOver = false;
            int iRetValue;
            bool isTimeout;
            double[] adValueX = new double[uiResolution];
            double[] adValueZ = new double[uiResolution];
            double ROIL = -100000;
            double ROIR = 100000;
            //实时显示图像
            //myModel = new PlotModel { Title = "Display Profiles" };

            myLine.Color = OxyColors.Transparent;
            //myLine.StrokeThickness = 2;
            myLine.MarkerSize = 1;
            //myLine.MarkerStroke = OxyColors.DarkGreen;
            myLine.MarkerFill = OxyColors.Black;
            myLine.MarkerType = MarkerType.Circle;
            myLine1.Color = OxyColors.Transparent;
            //myLine.StrokeThickness = 2;
            myLine1.MarkerSize = 0.7;
            //myLine.MarkerStroke = OxyColors.DarkGreen;
            myLine1.MarkerFill = OxyColors.Gray;
            myLine1.MarkerType = MarkerType.Circle;

            // Allocate the profile buffer to the maximal profile size times the profile count
            abyProfileBuffer = new byte[uiResolution * 64 * uiNeededProfileCount];
            byte[] abyTimestamp = new byte[16];

            // Start continous profile transmission
            ReadLog("Enable the measurement");
            if ((iRetValue = CLLTI.TransferProfiles(hLLT, CLLTI.TTransferProfileType.NORMAL_TRANSFER, 1)) < CLLTI.GENERAL_FUNCTION_OK)
            {
                OnError("Error during TransferProfiles", iRetValue);
                return;
            }

            // Wait for profile event (or timeout)
            ReadLog("Wait for needed profiles");
            while (!IsOver)
            {
                if (ROIChanged)
                {
                    ROIL = ROIX[0] < ROIX[1] ? ROIX[0] : ROIX[1];
                    ROIR = ROIX[0] > ROIX[1] ? ROIX[0] : ROIX[1];
                    myLine.Points.Clear();
                    myLine1.Points.Clear();
                    if (ROIState <2)
                    {
                        for (int j = 0; j < adValueX.Length; j++)
                        {
                            myLine.Points.Add(new DataPoint(adValueX[j], adValueZ[j]));
                        }
                    }
                    else
                    {
                        for (int j = 0; j < adValueX.Length; j++)
                        {
                            if (adValueX[j] > ROIL && adValueX[j] < ROIR)
                            {
                                myLine.Points.Add(new DataPoint(adValueX[j], adValueZ[j]));
                            }
                            else
                            {
                                myLine1.Points.Add(new DataPoint(adValueX[j], adValueZ[j]));
                            }
                        }
                    }
                    myModel.InvalidatePlot(true);
                    ROIChanged = false;
                }
                while (!IsOver&& !ROIChanged)
                {
                    isTimeout = false;

                    if (hProfileEvent.WaitOne(1) != true)
                    {
                        isTimeout = true;
                    }
                    if (!isTimeout)
                    {
                        // Test the size of profile
                        if (uiProfileDataSize == uiResolution * 64)
                            ReadLog("Profile size is OK");
                        else
                        {
                            ReadLog("Profile size is wrong");
                            continue;
                        }

                        // Convert partial profile to x and z values
                        ReadLog("Converting of profile data from the first reflection");
                        iRetValue = CLLTI.ConvertProfile2Values(hLLT, abyProfileBuffer, uiResolution, CLLTI.TProfileConfig.PROFILE, tscanCONTROLType,
                          0, 1, null, null, null, adValueX, adValueZ, null, null);
                        if (((iRetValue & CLLTI.CONVERT_X) == 0) || ((iRetValue & CLLTI.CONVERT_Z) == 0))
                        {
                            OnError("Error during Converting of profile data", iRetValue);
                            continue;
                        }

                        dataSaveX.Add(adValueX);
                        dataSaveZ.Add(adValueZ);
                        //// Display x and z values
                        //DisplayProfile(adValueX, adValueZ, uiResolution);

                        // Extract the 16-byte timestamp from the profile buffer into timestamp buffer and display it
                        Buffer.BlockCopy(abyProfileBuffer, 64 * (int)uiResolution - 16, abyTimestamp, 0, 16);
                        DisplayTimestamp(abyTimestamp);
                        uiProfileDataSize = 0;
                        uiReceivedProfileCount = 0;
                        myLine.Points.Clear();
                        myLine1.Points.Clear();
                        if (ROIState <2)
                        {                           
                            for (int j = 0; j < adValueX.Length; j++)
                            {
                                myLine.Points.Add(new DataPoint(adValueX[j], adValueZ[j]));
                            }
                        }
                        else
                        {                            
                            for (int j = 0; j < adValueX.Length; j++)
                            {
                                if (adValueX[j]>ROIL&&adValueX[j]<ROIR)
                                {
                                    myLine.Points.Add(new DataPoint(adValueX[j], adValueZ[j]));
                                }
                                else
                                {
                                    myLine1.Points.Add(new DataPoint(adValueX[j], adValueZ[j]));
                                }   
                            }
                        }
                        myModel.InvalidatePlot(true);
                    }
                }
                
            }
            if (!IsClosed) { ReadLog("Start data output."); }
            
            using (StreamWriter sw = new StreamWriter("dataTest.txt"))
            {

                for (int i = 0; i < dataSaveX.Count(); i++)
                {
                    for (int j = 0; j < dataSaveX[i].Length; j++)
                    {
                        sw.Write(dataSaveX[i][j].ToString());
                        sw.Write(" " + dataSaveZ[i][j].ToString()+'\n');
                    }
                }
            }
            if (!IsClosed){ ReadLog("Data output completed."); }

            if (bConnected)
            {
                if (IsClosed)
                {
                    if ((iRetValue = CLLTI.TransferProfiles(hLLT, CLLTI.TTransferProfileType.NORMAL_TRANSFER, 0)) < CLLTI.GENERAL_FUNCTION_OK)
                    {
                    }
                    if ((iRetValue = CLLTI.Disconnect(hLLT)) < CLLTI.GENERAL_FUNCTION_OK)
                    {
                    }
                    if ((iRetValue = CLLTI.DelDevice(hLLT)) < CLLTI.GENERAL_FUNCTION_OK)
                    {
                    }
                    bConnected = false;
                }
                else
                {
                    ReadLog("\n----- Disconnect from scanCONTROL -----\n");

                    ReadLog("Disable the measurement");
                    if ((iRetValue = CLLTI.TransferProfiles(hLLT, CLLTI.TTransferProfileType.NORMAL_TRANSFER, 0)) < CLLTI.GENERAL_FUNCTION_OK)
                    {
                        OnError("Error during TransferProfiles", iRetValue);

                    }

                    // Disconnect from the sensor
                    ReadLog("Disconnect the scanCONTROL");
                    if ((iRetValue = CLLTI.Disconnect(hLLT)) < CLLTI.GENERAL_FUNCTION_OK)
                    {
                        OnError("Error during Disconnect", iRetValue);
                    }

                    // Free ressources
                    ReadLog("Delete the scanCONTROL instance");
                    if ((iRetValue = CLLTI.DelDevice(hLLT)) < CLLTI.GENERAL_FUNCTION_OK)
                    {
                        OnError("Error during Delete", iRetValue);
                    }
                    bConnected = false;
                }
                
            }

            //for (int i = 0; i < dataSaveX.Count(); i++)
            //{
            //    myLine.Points.Clear();
            //    myModel.InvalidatePlot(true);
            //    Thread.Sleep(1000);
            //    for (int j = 0; j < dataSaveX[0].Length; j++)
            //    {

            //        myLine.Points.Add(new DataPoint(dataSaveX[i][j], dataSaveZ[i][j]));

            //    }
            //    myModel.InvalidatePlot(true);

            //    Thread.Sleep(1000);
            //}
            //Thread.Sleep(1000);
            //ReadLog("Test done.");
        }

        // Display the timestamp
        static void DisplayTimestamp(byte[] abyTimestamp)
        {
            double dShutterOpen = 0, dShutterClose = 0;
            uint uiProfileCount = 0;

            //Decode the timestamp
            CLLTI.Timestamp2TimeAndCount(abyTimestamp, ref dShutterOpen, ref dShutterClose, ref uiProfileCount);
            ReadLog("ShutterOpen: " + dShutterOpen + " ShutterClose: " + dShutterClose);
            ReadLog("ProfileCount: " + uiProfileCount);
        }

        // Display the error text
        static void OnError(string strErrorTxt, int iErrorValue)
        {
            byte[] acErrorString = new byte[200];

            ReadLog(strErrorTxt);
            if (CLLTI.TranslateErrorValue(hLLT, iErrorValue, acErrorString, acErrorString.GetLength(0))
                                            >= CLLTI.GENERAL_FUNCTION_OK)
                ReadLog(System.Text.Encoding.ASCII.GetString(acErrorString, 0, acErrorString.GetLength(0)));
        }

        [DllImport("msvcrt.dll", EntryPoint = "memcpy", CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        static unsafe extern IntPtr memcpy(byte* dest, byte* src, uint count);

        /*
         * Callback function which copies the received data into the buffer and sets an event after the specified profiles
         */
        static unsafe void ProfileEvent(byte* data, uint uiSize, uint uiUserData)
        {
            if (uiSize > 0)
            {
                if (uiReceivedProfileCount < uiNeededProfileCount)
                {
                    //If the needed profile count not arrived: copy the new Profile in the buffer and increase the recived buffer count
                    uiProfileDataSize = uiSize;
                    //Kopieren des Unmanaged Datenpuffers (data) in die Anwendung
                    fixed (byte* dst = &abyProfileBuffer[uiReceivedProfileCount * uiSize])
                    {
                        memcpy(dst, data, uiSize);
                    }
                    uiReceivedProfileCount++;
                }

                if (uiReceivedProfileCount >= uiNeededProfileCount)
                {
                    //If the needed profile count is arived: set the event
                    hProfileEvent.Set();
                }
            }
        }


        static public void delagateFunAppendText(string log)
        {
            form1.richTextBox1.AppendText(log);
        }
        static public void invokeFunAppendText(string log)
        {
            form1.Invoke(calDelegate1, log);
        }

        static public void ReadLog(string log)
        {
            string Time = Convert.ToString(DateTime.Now);
            form1.Invoke(calDelegate1, Time + "  " + log + "\n");
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if(button1.Text=="连接传感器")
            {
                button1.Text = "断开传感器";
                calDelegate1 = new calDelegate(delagateFunAppendText);
                calThread = new Thread(scanCONTROL_Sample);
                calThread.Start();
                Thread.Sleep(100);
            }
            else if (button1.Text == "断开传感器")
            {
                button1.Text = "连接传感器";
                IsOver = true;
                Thread.Sleep(100);
            }
            this.groupBox1.Focus();
        }

        //static private void ReadLog(string log)
        //{
        //    form1.richTextBox1.Invoke(calDelegate1,log);
        //}

        private void richTextBox1_TextChanged(object sender, EventArgs e)
        {

        }

        private void button2_Click(object sender, EventArgs e)
        {
            ROIChanged = true;
            if (button2.Text=="设置ROI区域")
            {
                StartROISet = true;
                ROI1.Color = OxyColors.OrangeRed;
                ROI2.Color = OxyColors.CornflowerBlue;
                myModel.InvalidatePlot(true);
                button2.Text = "锁定ROI区域";
                button3.Visible = true;
            }
            else if (button2.Text == "锁定ROI区域")
            {
                StartROISet = false;
                ROI1.Color = OxyColors.Gray;
                ROI1.StrokeThickness = 0.01;
                ROI2.Color = OxyColors.Gray;
                ROI1.StrokeThickness = 0.01;
                myModel.InvalidatePlot(true);
                button2.Text = "设置ROI区域";
                Properties.Settings.Default.ROIX0 = ROIX[0];
                Properties.Settings.Default.ROIX1 = ROIX[1];
                Properties.Settings.Default.ROIState = ROIState;
                Properties.Settings.Default.Save();
                button3.Visible = false;
            }
            this.groupBox1.Focus();
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {
            //string str = textBox1.Text;
            //uint uiExposureTime = Convert.ToUInt32(str);
            //if (bOK)
            //{
            //    ReadLog("Set exposure time to " + uiExposureTime);
            //    if ((iRetValue = CLLTI.SetFeature(hLLT, CLLTI.FEATURE_FUNCTION_EXPOSURE_TIME, uiExposureTime)) < CLLTI.GENERAL_FUNCTION_OK)
            //    {
            //        OnError("Error during SetFeature(FEATURE_FUNCTION_EXPOSURE_TIME)", iRetValue);
            //        bOK = false;
            //    }
            //}
        }
        private void textBox1_LostFocus(object sender, EventArgs e)
        {
            if (!String.IsNullOrEmpty(textBox1.Text))
            {
                try
                {
                    string str = textBox1.Text;
                    uiExposureTimeTmp = Convert.ToDouble(str);
                    uiExposureTime = Convert.ToUInt32(uiExposureTimeTmp*100);
                }
                catch
                {
                    richTextBox1.AppendText("\n输入错误\n");
                    return;
                }
                if (uiExposureTimeTmp<0.01)
                {
                    richTextBox1.AppendText("\n输入错误\n");
                    uiExposureTimeTmp = 0;
                    return;
                }
                if (button1.Text=="断开传感器")
                {
                    ReadLog("Set exposure time to " + uiExposureTimeTmp+"ms");
                    if ((iRetValue = CLLTI.SetFeature(hLLT, CLLTI.FEATURE_FUNCTION_EXPOSURE_TIME, uiExposureTime)) < CLLTI.GENERAL_FUNCTION_OK)
                    {
                        OnError("Error during SetFeature(FEATURE_FUNCTION_EXPOSURE_TIME)", iRetValue);
                        bOK = false;
                    }
                }
                
                
                Properties.Settings.Default.uiExposureTime = uiExposureTime;
                Properties.Settings.Default.uiExposureTimeTmp = uiExposureTimeTmp;
                Properties.Settings.Default.Save();
            }
            
        }

        private void Form1_MouseClick(object sender, MouseEventArgs e)
        {
            groupBox1.Focus();
        }

        private void textBox1_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar != '\b'&&e.KeyChar != '.')//这是允许输入退格键
            {
                if ((e.KeyChar < '0') || (e.KeyChar > '9'))//这是允许输入0-9数字
                {
                    e.Handled = true;
                }
            }
        }
        private void textBox1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                groupBox1.Focus();
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            //载入用户数据
            uiExposureTime = Properties.Settings.Default.uiExposureTime;
            uiExposureTimeTmp = Properties.Settings.Default.uiExposureTimeTmp;
            ROIX[0]=Properties.Settings.Default.ROIX0;
            ROIX[1]=Properties.Settings.Default.ROIX1;
            ROIState=Properties.Settings.Default.ROIState;
            if(ROIState>1)
            {
                ROI1.Points.Clear();
                ROI2.Points.Clear();
                ROI1.Points.Add(new DataPoint(ROIX[0], -100000));
                ROI1.Points.Add(new DataPoint(ROIX[0], 100000));
                ROI2.Points.Add(new DataPoint(ROIX[1], -100000));
                ROI2.Points.Add(new DataPoint(ROIX[1], 100000));
            }
            textBox1.Text = Convert.ToString(uiExposureTimeTmp);
            myModel = new PlotModel { Title = "Display Profiles" };
            form1.plotView1.Model = myModel;
            //y轴
            yValueAxis.IntervalLength = 20;
            yValueAxis.Angle = 60;
            yValueAxis.IsZoomEnabled = true;
            yValueAxis.IsPanEnabled = true;
            yValueAxis.Maximum = 140;
            yValueAxis.Minimum = 50;
            yValueAxis.Title = "Distance Z[mm]";
            //x轴
            xValueAxis.Position = AxisPosition.Bottom;
            xValueAxis.Minimum = -65;
            xValueAxis.Maximum = 65;
            //TitlePosition = 5,
            xValueAxis.IntervalLength = 60;
            xValueAxis.Title = "Position X[mm]";
            //MinorIntervalType = DateTimeIntervalType.Seconds,
            //IntervalType = DateTimeIntervalType.Seconds,
            //MajorGridlineStyle = LineStyle.Solid,
            //MinorGridlineStyle = LineStyle.None,
            myModel.Series.Add(myLine);
            myModel.Series.Add(myLine1);

            ROI1.LineStyle = LineStyle.Solid;
            ROI2.LineStyle = LineStyle.Solid;
            ROI1.Color = OxyColors.Gray;
            ROI1.StrokeThickness = 0.01;
            ROI2.Color = OxyColors.Gray;
            ROI2.StrokeThickness = 0.01;
            myModel.Axes.Add(yValueAxis);
            myModel.Axes.Add(xValueAxis);
            myModel.Series.Add(ROI1);
            myModel.Series.Add(ROI2);
            ROI1.CanTrackerInterpolatePoints=false;
            ROI2.CanTrackerInterpolatePoints = false;
            myModel.InvalidatePlot(true);

            myModel.MouseDown += myModel_MouseDown;
            //myModel.MouseUp += myModel_Mouseup;
            myModel.MouseMove += myModel_MouseMove;
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            IsOver = true;
            IsClosed = true;
            Properties.Settings.Default.ROIX0 = ROIX[0];
            Properties.Settings.Default.ROIX1 = ROIX[1];
            Properties.Settings.Default.ROIState = ROIState;
            Properties.Settings.Default.uiExposureTime = uiExposureTime;
            Properties.Settings.Default.uiExposureTimeTmp = uiExposureTimeTmp;
            Properties.Settings.Default.Save();

        }

        private void myModel_MouseDown(object sender, OxyMouseDownEventArgs ex)
        {
            if (StartROISet&&ex.ChangedButton==OxyMouseButton.Left)
            {
                PlotMouseDown = true;
                double dataTmp = xValueAxis.InverseTransform(ex.Position.X);

                if (ROIState == 0)
                {
                    ROI1.Points.Add(new DataPoint(dataTmp, -100000));
                    ROI1.Points.Add(new DataPoint(dataTmp, 100000));
                    ROIX[0] = dataTmp;
                    ROI1.Color = OxyColors.Gray;
                    ROI1.StrokeThickness = 0.5;
                    myModel.InvalidatePlot(true);
                }
                else if (ROIState == 1)
                {
                    ROIX[1] = dataTmp;
                    ROI2.Points.Add(new DataPoint(ROIX[1], -100000));
                    ROI2.Points.Add(new DataPoint(ROIX[1], 100000));
                    ROI2.Color = OxyColors.Gray;
                    ROI2.StrokeThickness = 0.5;
                    myModel.InvalidatePlot(true);
                }
                else
                {
                    if (System.Math.Abs(dataTmp - ROIX[0]) < 2)
                    {
                        ROI1.Points.Clear();
                        ROIX[0] = dataTmp;
                        ROI1.Points.Add(new DataPoint(ROIX[0], -100000));
                        ROI1.Points.Add(new DataPoint(ROIX[0], 100000));
                        ROI1.Color = OxyColors.Gray;
                        ROI1.StrokeThickness = 0.5;
                        myModel.InvalidatePlot(true);
                        ROIState = 2;
                    }
                    else if (System.Math.Abs(dataTmp - ROIX[1]) < 2)
                    {
                        ROI2.Points.Clear();
                        ROIX[1] = dataTmp;
                        ROI2.Points.Add(new DataPoint(ROIX[1], -100000));
                        ROI2.Points.Add(new DataPoint(ROIX[1], 100000));
                        ROI2.Color = OxyColors.Gray;
                        ROI2.StrokeThickness = 0.5;
                        myModel.InvalidatePlot(true);
                        ROIState = 3;
                    }
                }
            }
        }
        private void myModel_MouseMove(object sender, OxyMouseEventArgs ex)
        {
            if(PlotMouseDown&&StartROISet)
            {
                double dataTmp = xValueAxis.InverseTransform(ex.Position.X);
                switch (ROIState)
                {
                    case 0:
                    case 2:
                        ROI1.Points.Clear();
                        ROI1.Points.Add(new DataPoint(dataTmp, -100000));
                        ROI1.Points.Add(new DataPoint(dataTmp, 100000));
                        ROIX[0] = dataTmp;
                        myModel.InvalidatePlot(true);
                        break;
                    case 1:
                    case 3:
                        ROI2.Points.Clear();
                        ROI2.Points.Add(new DataPoint(dataTmp, -100000));
                        ROI2.Points.Add(new DataPoint(dataTmp, 100000));
                        ROIX[1] = dataTmp;
                        myModel.InvalidatePlot(true);
                        break;
                }
            }
        }

        private void plotView1_MouseDown(object sender, MouseEventArgs e)
        {




        }


        private void plotView1_MouseUp(object sender, MouseEventArgs e)
        {
            if(e.Button==MouseButtons.Left&&StartROISet)
            {
                PlotMouseDown = false;
                ROI1.Color = OxyColors.OrangeRed;
                ROI2.Color = OxyColors.CornflowerBlue;
                ROI1.StrokeThickness = 1;
                ROI2.StrokeThickness = 1;
                myModel.InvalidatePlot(true);
                switch (ROIState)
                {
                    case 0:
                        ROIState = 1;
                        break;
                    case 1:
                    case 2:
                    case 3:
                        ROIState = 4;
                        break;
                }
                ROIChanged = true;
            } 
        }


        private void plotView1_MouseMove(object sender, MouseEventArgs e)
        {
            if(PlotMouseDown&& StartROISet)
            {
                

            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            ROIState = 0;
            ROI1.Points.Clear();
            ROI2.Points.Clear();
            myModel.InvalidatePlot(true);
            this.groupBox1.Focus();
        }
    }
}

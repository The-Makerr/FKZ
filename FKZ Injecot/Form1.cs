using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using DiscordRPC;
using DiscordRPC.Logging;
using static System.Windows.Forms.AxHost;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ListView;


namespace FKZ_Injecot
{
    public partial class Form1 : Form
    {
        private bool isDragging = false;
        private Point dragStartPosition;
        private List<Process> processes;
        // DLL injection constants
        private const int PROCESS_CREATE_THREAD = 0x0002;
        private const int PROCESS_QUERY_INFORMATION = 0x0400;
        private const int PROCESS_VM_OPERATION = 0x0008;
        private const int PROCESS_VM_WRITE = 0x0020;
        private const int PROCESS_VM_READ = 0x0010;
        private DiscordRpcClient rpcClient;
        private StringBuilder logBuilder = new StringBuilder(); // StringBuilder for log messages

        // External function declarations
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out int lpNumberOfBytesWritten);

        [DllImport("kernel32.dll")]
        private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);



        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);
        private List<string> dllFiles;

        public Form1()
        {
            InitializeComponent();

        }

        private void Form1_Load(object sender, EventArgs e)
        {

            logBuilder = new StringBuilder();

            // Retrieve the updated list of running processes
            processes = new List<Process>(Process.GetProcesses());

            processes.Sort((x, y) => string.Compare(x.ProcessName, y.ProcessName));

            // Display the updated list of running processes in the comboBox1 control
            foreach (var process in processes)
            {
                comboBox1.Items.Add($"{process.ProcessName} (ID: {process.Id})");
            }


            dllFiles = new List<string>();
          

            // Allow dropping DLL files on the ListBox
            listBox1.AllowDrop = true;
            listBox1.DragEnter += dllListBox_DragEnter;
            listBox1.DragDrop += dllListBox_DragDrop;
            // Change Button Color
            button5.MouseEnter += (s, ev) => button5.BackColor = Color.Gray;
            button5.MouseLeave += (s, ev) => button5.BackColor = Color.Black;

            button4.MouseEnter += (s, ev) => button4.BackColor = Color.Red;
            button4.MouseLeave += (s, ev) => button4.BackColor = Color.Black;

            panel1.MouseDown += Panel1_MouseDown;
            panel1.MouseMove += Panel1_MouseMove;
            panel1.MouseUp += Panel1_MouseUp;
            rpcClient = new DiscordRpcClient("1123641805880164412");
            rpcClient.Logger = new ConsoleLogger() { Level = LogLevel.Warning };
            rpcClient.Initialize();
            LogMessage("Form loaded.");
            LogMessage("Discord Presence Initialised.");

        }

        private void LogMessage(string message)
        {
            string logEntry = $"[{DateTime.Now.ToString("HH:mm")}] {message}";
            Console.WriteLine(logEntry); // Print to console (optional)

            // Append log message to the TextBox
            logBuilder.AppendLine(logEntry);
            richTextBox1.Text = logBuilder.ToString();
        }

        public void UpdateDiscordPresence(string state, string details)
        {
                var presence = new RichPresence()
                {
                    State = state,
                    //Details = details,
                    Timestamps = new Timestamps()
                    {
                        Start = DateTime.UtcNow
                    },
                    Assets = new Assets()
                    {
                        LargeImageKey = "discord_logo",
                    }
                };

                rpcClient.SetPresence(presence);
        }
        public void DisposeDiscordRPC()
        {
            rpcClient.Dispose();
        }

        private void Panel1_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isDragging = true;
                dragStartPosition = new Point(e.X, e.Y);
            }
        }

        private void Panel1_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDragging)
            {
                Point currentPosition = PointToScreen(e.Location);
                Location = new Point(currentPosition.X - dragStartPosition.X, currentPosition.Y - dragStartPosition.Y);
            }
        }

        private void Panel1_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isDragging = false;
            }
        }
        private void dllListBox_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }


        private void dllListBox_DragDrop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0)
            {
                foreach (string file in files)
                {
                    dllFiles.Add(file);

                    FileInfo fileInfo = new FileInfo(file);
                    string fileName = fileInfo.Name;
                    string fullPath = fileInfo.FullName;
                    string architecture = GetDllArchitecture(file); // Custom method to get the architecture

                    string displayText = $"{fileName} | {fullPath} | ({architecture})";

                    listBox1.Items.Add(displayText);
                }
            }
        }

        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (comboBox1.SelectedIndex != -1)
            {
                LogMessage("Injection Started.");
                if (comboBox1.SelectedItem is string selectedProcessString)
                {
                    if (int.TryParse(selectedProcessString.Substring(selectedProcessString.LastIndexOf("(") + 4, selectedProcessString.Length - selectedProcessString.LastIndexOf("(") - 5), out int selectedProcessId))
                    {
                        Process selectedProcess = Process.GetProcessById(selectedProcessId);

                        // Inject the DLL files into the selected process
                        bool injectionSuccess = InjectDLL(selectedProcess, dllFiles);
                        if (injectionSuccess)
                        {
                            LogMessage("Injected successfully.");                        
                            MessageBox.Show("DLLs injected successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            LogMessage("Injection Ended.");
                        }
                        else
                        {
                            LogMessage("DLL injection failed.");
                            MessageBox.Show("DLL injection failed.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                    else
                    {
                        LogMessage("Failed to parse the process ID.");
                        MessageBox.Show("Failed to parse the process ID.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else
                {
                    LogMessage("Failed to retrieve the selected process.");
                    MessageBox.Show("Failed to retrieve the selected process.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                LogMessage("Please select a process.");
                MessageBox.Show("Please select a process.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private bool InjectDLL(Process process, List<string> dllFiles)
        {
            // Open the target process with desired access rights
            IntPtr processHandle = OpenProcess(PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ, false, process.Id);
            if (processHandle == IntPtr.Zero)
            {
                LogMessage($"Failed to open process (ID: {process.Id})");
                MessageBox.Show($"Failed to open process (ID: {process.Id})", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            // Get the address of the LoadLibrary function in the target process
            IntPtr kernel32Module = GetModuleHandle("kernel32.dll");
            if (kernel32Module == IntPtr.Zero)
            {
                LogMessage("Failed to get handle for kernel32.dll.");
                MessageBox.Show("Failed to get handle for kernel32.dll", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                CloseHandle(processHandle);
                LogMessage("ClosedHandle.");
                return false;
            }

            IntPtr loadLibraryAddr = GetProcAddress(kernel32Module, "LoadLibraryA");
            if (loadLibraryAddr == IntPtr.Zero)
            {
                LogMessage("Failed to get address of LoadLibraryA.");
                MessageBox.Show("Failed to get address of LoadLibraryA", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                CloseHandle(processHandle);
                LogMessage("ClosedHandle.");
                return false;
            }

            foreach (string dllFile in dllFiles)
            {
                // Allocate memory in the target process for the DLL path
                IntPtr dllPathAddr = VirtualAllocEx(processHandle, IntPtr.Zero, (uint)dllFile.Length, 0x1000, 0x40);
                if (dllPathAddr == IntPtr.Zero)
                {
                    LogMessage("Failed to allocate memory in the target process");
                    MessageBox.Show("Failed to allocate memory in the target process", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    CloseHandle(processHandle);
                    LogMessage("ClosedHandle.");
                    return false;
                }

                // Write the DLL path into the target process's memory
                byte[] dllPathBytes = System.Text.Encoding.ASCII.GetBytes(dllFile);
                int bytesWritten;
                if (!WriteProcessMemory(processHandle, dllPathAddr, dllPathBytes, (uint)dllPathBytes.Length, out bytesWritten))
                {
                    LogMessage("Failed to write the DLL path into the target process's memory.");
                    MessageBox.Show("Failed to write the DLL path into the target process's memory", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    CloseHandle(processHandle);
                    LogMessage("ClosedHandle.");
                    return false;
                }

                // Create a remote thread in the target process to load the DLL
                IntPtr threadHandle = CreateRemoteThread(processHandle, IntPtr.Zero, 0, loadLibraryAddr, dllPathAddr, 0, IntPtr.Zero);
                if (threadHandle == IntPtr.Zero)
                {
                    LogMessage("Failed to create a remote thread in the target process.");
                    MessageBox.Show("Failed to create a remote thread in the target process", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    CloseHandle(processHandle);
                    LogMessage("ClosedHandle.");
                    return false;
                }

                // Wait for the remote thread to finish
                WaitForSingleObject(threadHandle, 0xFFFFFFFF);

                CloseHandle(threadHandle);
            }

            CloseHandle(processHandle);


            return true;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);
        private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void label3_Click(object sender, EventArgs e)
        {

        }


        private string GetDllArchitecture(string filePath)
        {
            // You can use the System.Runtime.InteropServices.RuntimeInformation class
            // to get the architecture of the DLL file.
            bool is64BitProcess = Environment.Is64BitProcess;
            bool is64BitDll = IntPtr.Size == 8;

            return is64BitProcess == is64BitDll ? "x64" : "x86";
        }

        private void button4_Click(object sender, EventArgs e)
        {

            // Close the application
            DisposeDiscordRPC();
            Application.Exit();
        }

        private void button5_Click(object sender, EventArgs e)
        {
            this.WindowState = FormWindowState.Minimized;
        }

        private void panel1_Paint(object sender, PaintEventArgs e)
        {

        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBox1.SelectedIndex != -1)
            {
                int selectedProcessIndex = comboBox1.SelectedIndex;
                Process selectedProcess = processes[selectedProcessIndex];
                UpdateDiscordPresence("Injecting files into: " + comboBox1.SelectedItem, "FKZ Injector");
            }
        }

        private void panel3_Paint(object sender, PaintEventArgs e)
        {

        }

        private void label3_Click_1(object sender, EventArgs e)
        {

        }

        private void label7_Click(object sender, EventArgs e)
        {

        }

        private void label5_Click(object sender, EventArgs e)
        {

        }

        private void richTextBox1_TextChanged(object sender, EventArgs e)
        {

        }

        private void button3_Click(object sender, EventArgs e)
        {
            listBox1.Items.Clear();
        }

        private void button2_Click_1(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "DLL Files|*.dll";
            openFileDialog.Multiselect = true;

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                foreach (string filePath in openFileDialog.FileNames)
                {
                    FileInfo fileInfo = new FileInfo(filePath);
                    string fileName = fileInfo.Name;
                    string fullPath = fileInfo.FullName;
                    string architecture = GetDllArchitecture(filePath); // Custom method to get the architecture

                    string displayText = $"{fileName} | {fullPath} | ({architecture})";

                    listBox1.Items.Add(displayText);
                }
            }
        }

        private void button6_Click(object sender, EventArgs e)
        {
            if (comboBox1.SelectedIndex != -1)
            {
                if (comboBox1.SelectedItem is string selectedProcessString)
                {
                    if (int.TryParse(selectedProcessString.Substring(selectedProcessString.LastIndexOf("(") + 4, selectedProcessString.Length - selectedProcessString.LastIndexOf("(") - 5), out int selectedProcessId))
                    {
                        Process selectedProcess = Process.GetProcessById(selectedProcessId);
                        // Kill the process
                        selectedProcess.Kill();

                        // Optionally, you can wait for the process to exit and handle any cleanup or error scenarios
                        selectedProcess.WaitForExit();

                        // Process killed successfully
                        LogMessage("Process killed successfully.");
                        MessageBox.Show("Process killed successfully.");
                    }
                }
            }
            else
            {
                LogMessage("No process selected");
                MessageBox.Show("No process selected.");
            }
        }

        private void button7_Click(object sender, EventArgs e)
        {
            // Get the selected item from the combo box
            int selectedIndex = listBox1.SelectedIndex;

            // Unload the selected DLL
            if (selectedIndex >= 0)
            {
                IntPtr hModule = (IntPtr)listBox1.Items[selectedIndex];

                if (hModule != IntPtr.Zero)
                {
                    // Call the FreeLibrary function to unload the DLL
                    if (FreeLibrary(hModule))
                    {
                        // Unloading successful
                        MessageBox.Show("DLL unloaded successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        // Error occurred while unloading the DLL
                        int errorCode = Marshal.GetLastWin32Error();
                        MessageBox.Show("Error unloading DLL. Error code: " + errorCode, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else
                {
                    // Invalid handle
                    MessageBox.Show("Invalid DLL handle.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                // No DLL selected
                MessageBox.Show("No DLL selected.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

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

        // External function declarations
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

            // Retrieve the list of running processes
            processes = new List<Process>(Process.GetProcesses());

            processes.Sort((x, y) => string.Compare(x.ProcessName, y.ProcessName));

            // Display the list of running processes in the ListBox
            foreach (var process in processes)
            {
                comboBox1.Items.Add($"{process.ProcessName} (ID: {process.Id})");
            }

            dllFiles = new List<string>();
          

            // Allow dropping DLL files on the ListBox
            listBox2.AllowDrop = true;
            listBox2.DragEnter += dllListBox_DragEnter;
            listBox2.DragDrop += dllListBox_DragDrop;
            // Change Button Color
            button5.MouseEnter += (s, ev) => button5.BackColor = Color.Gray;
            button5.MouseLeave += (s, ev) => button5.BackColor = Color.Black;

            button4.MouseEnter += (s, ev) => button4.BackColor = Color.Red;
            button4.MouseLeave += (s, ev) => button4.BackColor = Color.Black;

            panel1.MouseDown += Panel1_MouseDown;
            panel1.MouseMove += Panel1_MouseMove;
            panel1.MouseUp += Panel1_MouseUp;


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
                    listBox2.Items.Add(file);
                }
            }
        }

        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (comboBox1.SelectedIndex != -1 && listBox2.SelectedIndex != -1)
            {
                if (comboBox1.SelectedItem is string selectedProcessString && listBox2.SelectedItem is string selectedDllFile)
                {
                    if (int.TryParse(selectedProcessString.Substring(selectedProcessString.LastIndexOf("(") + 4, selectedProcessString.Length - selectedProcessString.LastIndexOf("(") - 5), out int selectedProcessId))
                    {
                        Process selectedProcess = Process.GetProcessById(selectedProcessId);

                        // Inject the DLL into the selected process
                        bool injectionSuccess = InjectDLL(selectedProcess, selectedDllFile);
                        if (injectionSuccess)
                        {
                            MessageBox.Show("DLL injected successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            MessageBox.Show("DLL injection failed.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                    else
                    {
                        MessageBox.Show("Failed to parse the process ID.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else
                {
                    MessageBox.Show("Failed to retrieve the selected process and DLL file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                MessageBox.Show("Please select a process and a DLL file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private bool InjectDLL(Process process, string dllPath)
        {
            // Open the target process with desired access rights
            IntPtr processHandle = OpenProcess(PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ, false, process.Id);
            if (processHandle == IntPtr.Zero)
            {
                MessageBox.Show($"Failed to open process (ID: {process.Id})", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            // Get the address of the LoadLibrary function in the target process
            IntPtr kernel32Module = GetModuleHandle("kernel32.dll");
            if (kernel32Module == IntPtr.Zero)
            {
                MessageBox.Show("Failed to get handle for kernel32.dll", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                CloseHandle(processHandle);
                return false;
            }

            IntPtr loadLibraryAddr = GetProcAddress(kernel32Module, "LoadLibraryA");
            if (loadLibraryAddr == IntPtr.Zero)
            {
                MessageBox.Show("Failed to get address of LoadLibraryA", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                CloseHandle(processHandle);
                return false;
            }

            // Allocate memory in the target process for the DLL path
            IntPtr dllPathAddr = VirtualAllocEx(processHandle, IntPtr.Zero, (uint)dllPath.Length, 0x1000, 0x40);
            if (dllPathAddr == IntPtr.Zero)
            {
                MessageBox.Show("Failed to allocate memory in the target process", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                CloseHandle(processHandle);
                return false;
            }

            // Write the DLL path into the target process's memory
            byte[] dllPathBytes = System.Text.Encoding.ASCII.GetBytes(dllPath);
            int bytesWritten;
            if (!WriteProcessMemory(processHandle, dllPathAddr, dllPathBytes, (uint)dllPathBytes.Length, out bytesWritten))
            {
                MessageBox.Show("Failed to write the DLL path into the target process's memory", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                CloseHandle(processHandle);
                return false;
            }

            // Create a remote thread in the target process to load the DLL
            IntPtr threadHandle = CreateRemoteThread(processHandle, IntPtr.Zero, 0, loadLibraryAddr, dllPathAddr, 0, IntPtr.Zero);
            if (threadHandle == IntPtr.Zero)
            {
                MessageBox.Show("Failed to create a remote thread in the target process", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                CloseHandle(processHandle);
                return false;
            }

            // Wait for the remote thread to finish
            WaitForSingleObject(threadHandle, 0xFFFFFFFF);

            CloseHandle(threadHandle);
            CloseHandle(processHandle);

            return true;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);
        private void listBox2_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void label3_Click(object sender, EventArgs e)
        {

        }

        private void button2_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "DLL Files|*.dll";
            openFileDialog.Multiselect = true;

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                foreach (string filePath in openFileDialog.FileNames)
                {
                    listBox2.Items.Add(filePath);
                }
            }
        }

        private void button4_Click(object sender, EventArgs e)
        { 
            // Close the application
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
            }
        }
    }
}

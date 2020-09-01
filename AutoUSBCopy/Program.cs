using System;
using System.IO;
using System.Management;
using System.Threading;

namespace AutoUSBCopy
{
    class Program
    {
        static void Main(string[] args)
        {
            StartProcedure();
        }

        private static void StartProcedure()
        {
            WqlEventQuery insertQuery =
                new WqlEventQuery(
                    "SELECT * FROM __InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_USBHub'");
            ManagementEventWatcher insertWatcher = new ManagementEventWatcher(insertQuery);
            insertWatcher.EventArrived += DeviceInsertedEvent;

            WqlEventQuery removeQuery =
                new WqlEventQuery(
                    "SELECT * FROM __InstanceDeletionEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_USBHub'");
            ManagementEventWatcher removeWatcher = new ManagementEventWatcher(removeQuery);
            removeWatcher.EventArrived += DeviceRemovedEvent;
            
            insertWatcher.Start();
            removeWatcher.Start();

            // Create a IPC wait handle with a unique identifier.
            var waitHandle = new EventWaitHandle(false, EventResetMode.AutoReset,
                "cae3d8d2-4fd2-4ae1-93a9-e7578b770ed5", out var createdNew);

            // If the handle was already there, inform the other process to exit itself.
            // Afterwards we'll also die.
            if (!createdNew)
            {
                waitHandle.Set();
                return;
            }

            waitHandle.WaitOne();
        }

        private static void DeviceInsertedEvent(object sender, EventArrivedEventArgs e)
        {
            try
            {
                ManagementBaseObject instance = (ManagementBaseObject) e.NewEvent["TargetInstance"];

                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string deviceID = (string) instance.GetPropertyValue("DeviceID");
                string serialNr = deviceID.Substring(deviceID.LastIndexOf('\\')).Replace("\\", "");
                string driveLetter = GetDriveLetter(serialNr);
                string destinationPath = string.Empty;
                string sourcePath = string.Empty;

                sourcePath = Path.GetFullPath(driveLetter);
                destinationPath = Path.Combine(appDataPath, serialNr);

                if (!Directory.Exists(destinationPath))
                    Directory.CreateDirectory(destinationPath);

                DirectoryCopy(sourcePath, destinationPath, true);

                Console.WriteLine("Drive letter " + driveLetter);
                Console.WriteLine("Files copied to {0}", destinationPath);
            }
            catch (Exception)
            {
            }
        }

        private static void DeviceRemovedEvent(object sender, EventArrivedEventArgs e)
        {
            //ManagementBaseObject instance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            //foreach (var property in instance.Properties)
            //{
            //    Console.WriteLine(property.Name + " = " + property.Value);
            //}
        }

        private static string GetDriveLetter(string serialNr)
        {
            var res = new ManagementObjectSearcher(@"SELECT * FROM Win32_DiskDrive WHERE SerialNumber ='" + serialNr + "'").Get();
            string drvLetter = string.Empty;

            foreach (ManagementObject device in new ManagementObjectSearcher(@"SELECT * FROM Win32_DiskDrive WHERE InterfaceType LIKE 'USB%'").Get())
            {
                if (serialNr == device.GetPropertyValue("SerialNumber").ToString())
                {
                    foreach (ManagementObject partition in new ManagementObjectSearcher(
                            "ASSOCIATORS OF {Win32_DiskDrive.DeviceID='" + device.Properties["DeviceID"].Value
                                                                         + "'} WHERE AssocClass = Win32_DiskDriveToDiskPartition")
                        .Get())
                    {
                        foreach (ManagementObject disk in new ManagementObjectSearcher(
                            "ASSOCIATORS OF {Win32_DiskPartition.DeviceID='"
                            + partition["DeviceID"]
                            + "'} WHERE AssocClass = Win32_LogicalDiskToPartition").Get())
                        {
                            drvLetter = disk.GetPropertyValue("Name").ToString();
                        }
                    }

                    break;
                }
            }

            return drvLetter;
        }

        private static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs)
        {
            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new DirectoryInfo(sourceDirName);

            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException(
                    "Source directory does not exist or could not be found: "
                    + sourceDirName);
            }

            DirectoryInfo[] dirs = dir.GetDirectories();
            // If the destination directory doesn't exist, create it.
            if (!Directory.Exists(destDirName))
            {
                Directory.CreateDirectory(destDirName);
            }

            // Get the files in the directory and copy them to the new location.
            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string temppath = Path.Combine(destDirName, file.Name);
                try
                {
                    file.CopyTo(temppath, true);
                }
                catch (Exception)
                {
                }
            }

            // If copying subdirectories, copy them and their contents to new location.
            if (copySubDirs)
            {
                foreach (DirectoryInfo subdir in dirs)
                {
                    string temppath = Path.Combine(destDirName, subdir.Name);
                    DirectoryCopy(subdir.FullName, temppath, copySubDirs);
                }
            }
        }
    }
}
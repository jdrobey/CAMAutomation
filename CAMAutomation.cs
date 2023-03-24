using System;
using System.IO;
using System.Diagnostics;
using System.Configuration;

namespace CAMAutomation
{
    public class CAMAutomation
    {
        public static int Main(string[] args)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            bool success = false;

            // Retrieve and set MISUMI General Tolerance
            Utilities.MathUtils.ABS_TOL = Double.TryParse(ConfigurationManager.AppSettings["MISUMI_GENERAL_TOLERANCE"], out double tolerance) && tolerance > 0.0 ? tolerance : 1.0E-6;
            
            // Create an instance of CAMAutomation manager
            CAMAutomationManager manager = CAMAutomationManager.GetInstance();

            // Get the UGII_TMP_DIR and set it as an output directory for the LogFile
            string tempFolderPath = Environment.GetEnvironmentVariable("UGII_TMP_DIR");
            if (tempFolderPath != null)
            {
                manager.LogFile.OutputPath = tempFolderPath;
            }

            // Check number of arguments
            if (args.Length == 2 || args.Length == 3)
            {
                // Read the arguments
                string inputFile = args[0];
                string outputDirectory = args[1];
                string url = args.Length == 3 ? args[2] : null;

                // Check the output directory first, so we can immediately assign the output path of the Log File
                if (Directory.Exists(outputDirectory))
                {
                    manager.LogFile.OutputPath = outputDirectory;

                    // Check if input file exists
                    if (File.Exists(inputFile))
                    {
                        // Initialize the Remote Session
                        if (Utilities.RemoteSession.Initialize(url))
                        {
                            // Execute the tasks
                            success = manager.Execute(inputFile, outputDirectory);
                        }
                        else
                        {
                            string msg = "Unable to connect to the NX Session";
                            manager.LogFile.AddError(msg);
                        }
                    }
                    else
                    {
                        string msg = "Input File does not exist";
                        manager.LogFile.AddError(msg);
                    }
                }
                else
                {
                    string msg = "Output Directory does not exist";
                    manager.LogFile.AddError(msg);
                }
            }
            else
            {
                string msg = "Invalid number of arguments";
                manager.LogFile.AddError(msg);
            }

            sw.Stop();
            manager.LogFile.AddMessage("Time Elapsed: " + sw.Elapsed.ToString());

            // Write the Log File
            manager.LogFile.Write();

            // Release the Manager
            CAMAutomationManager.Release();

            return success ? 0 : 1;
        }


        public static int GetUnloadOption(string dummy)
        {
            return (int)NXOpen.Session.LibraryUnloadOption.Immediately;
        }
    }
}

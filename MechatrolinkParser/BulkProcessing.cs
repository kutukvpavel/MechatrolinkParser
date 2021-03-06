﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace MechatrolinkParser
{
    /// <summary>
    /// Process multiple folders with multiple data files
    /// </summary>
    public static class BulkProcessing
    {
        /// <summary>
        /// Must start with {0} (it represents the path string + original file name) and end with {1} (represents extension with a dot)
        /// Any characters allowed in file names can be used in between
        /// </summary>
        public static string NameModificationFormat { get; set; } = "{0}_parsed{1}";
        public static bool UseParallelComputation { get; set; } = false;
        public static string ReportsExtension { get; set; } = ".ptxt";

        /// <summary>
        /// Invokes multiple MechatrolinkParser instances
        /// Spins multiple threads for efficiency (if UseParallelComputation is set to true)
        /// Tries to use all available cores first, then reduces thread pool size if OutOfMemory exceptions are raised
        /// Retries to parse files that failed with OutOfMemory exception (once)
        /// </summary>
        /// <param name="directory">Input directory (all inner directories are included into file search)</param>
        /// <param name="limit">Ordinary command line argument: time limit</param>
        /// <param name="freq">Ordinary command line argument: communication frequency</param>
        /// <param name="search">Limited to ? and * wildcards, for example "*.txt"</param>
        /// <param name="flags">String of command line switches that go after limit and frequency, separated with whitespaces</param>
        /// <returns>Success == true</returns>
        public static bool ProcessDirectory(string directory, int limit, int freq, string search, string flags)
        {
            bool success = true;
            //Get list of all suitable files
            string[] files = Directory.GetFiles(directory, search, SearchOption.AllDirectories);
            files = files.Where(x => x != NameModifier(x)).ToArray(); //Skip reports, in case their extensions coincide with data files
            string args = string.Format("/c \"{0} \"{{0}}\" {1} {2} {3} > \"{{1}}\"\"",
                string.Format("\"{0}\"", System.Reflection.Assembly.GetExecutingAssembly().Location),
                limit, freq, flags);
            int maxInstances = UseParallelComputation ? Environment.ProcessorCount : 1;
            List<Process> running = new List<Process>(maxInstances);
            List<ProcessStartInfo> retry = new List<ProcessStartInfo>();
            for (int i = 0; i < files.Length; i += maxInstances)
            {
                running.Clear();
                for (int j = 0; j < maxInstances; j++)
                {
                    if (i + j >= files.Length) break;
                    if (!CallInstance(running, files[i + j], args)) success = false;
                }
                WaitForThreadPool(running);
                foreach (var item in running)
                {
                    if (item.ExitCode != 0)
                    {
                        DataReporter.ReportProgress(
                            string.Format("Parser process returned an error {0}, arguments: {1}",
                            item.ExitCode, item.StartInfo.Arguments));
                        if (item.ExitCode == (int)Program.ExitCodes.OutOfMemory && maxInstances > 1)
                        {
                            retry.Add(item.StartInfo);
                            if (maxInstances > 1) maxInstances = (int)Math.Floor(maxInstances / 2d); //Adaptive parallelism
                            i += maxInstances; //Compensate for maxInstances change
                        }
                        else
                        {
                            success = false;
                        }
                    }
                }
                DataReporter.ReportProgress("Finished processing of the file (or group of files).");
            }
            running.Clear();
            if (retry.Any()) DataReporter.ReportProgress("Retrying to parse files that caused OutOfMemory exceptions...");
            foreach (var item in retry)
            {
                if (!CallInstance(running, item)) success = false;
                WaitForThreadPool(running);
                if (running.First().ExitCode != 0) success = false;
            }
            DataReporter.ReportProgress("Finished processing of all the files.");
            return success;
        }
        private static void WaitForThreadPool(List<Process> running)
        {
            while (running.Any(x => !x.HasExited))
            {
                Thread.Sleep(100);
            }
        }
        private static bool CallInstance(List<Process> running, ProcessStartInfo psi)
        {
            try
            {
                running.Add(Process.Start(psi));
                DataReporter.ReportProgress("Started processing: " + psi.Arguments);
            }
            catch (Exception e)
            {
                DataReporter.ReportProgress(string.Format(@"Failed to start process with arguments: {0}
Error details: {1}", psi.Arguments, e.ToString()));
                return false;
            }
            return true;
        }
        private static bool CallInstance(List<Process> running, string file, string args)
        {
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe");
            psi.Arguments = string.Format(args, file, Path.GetFileName(NameModifier(file)));
            psi.WorkingDirectory = Path.GetDirectoryName(file);
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            return CallInstance(running, psi);
        }
        private static string NameModifier(string original)
        {
            return original.Replace(Path.GetFileName(original),
                string.Format(NameModificationFormat, Path.GetFileNameWithoutExtension(original), ReportsExtension));
        }
    }
}

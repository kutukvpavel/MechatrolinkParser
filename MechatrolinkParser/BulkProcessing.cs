using System;
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
        /// Spins multiple threads for efficiency
        /// </summary>
        /// <param name="directory">Input directory (all inner directories are included into file search)</param>
        /// <param name="limit">Ordinary command line argument: time limit</param>
        /// <param name="freq">Ordinary command line argument: communication frequency</param>
        /// <param name="search">Limited to ? and * wildcards, for example "*.txt"</param>
        /// <param name="flags">String of command line switches that go after limit and frequency, separated with whitespaces</param>
        public static bool ProcessDirectory(string directory, int limit, int freq, string search, string flags)
        {
            bool success = true;
            //Get list of all suitable files
            string[] files = Directory.GetFiles(directory, search, SearchOption.AllDirectories);
            files = files.Where(x => x != NameModifier(x)).ToArray(); //Skip reports, in case their extensions coincide with data files
            string args = string.Format("/c \"{0} \"{{0}}\" {1} {2} {3} > {{1}}\"",
                string.Format("\"{0}\"", System.Reflection.Assembly.GetExecutingAssembly().Location),
                limit, freq, flags);
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe")
            {
                CreateNoWindow = true
            };
            if (UseParallelComputation)
            {
                //In case HT is enabled, divide by 2 (to prevent unneeded memory flooding)
                int maxInstances = Environment.ProcessorCount > 1 ? (int)Math.Floor(Environment.ProcessorCount / 2d) : 1;
                int i;
                List<Process> running = new List<Process>(maxInstances);
                for (i = 0; i < files.Length; i += maxInstances)
                {
                    running.Clear();
                    for (int j = 0; j < maxInstances; j++)
                    {
                        if (i + j >= files.Length) break;
                        psi.Arguments = string.Format(args, files[i + j], Path.GetFileName(NameModifier(files[i + j])));
                        psi.WorkingDirectory = Path.GetDirectoryName(files[i + j]);
                        try
                        {
                            running.Add(Process.Start(psi));
                            DataReporter.ReportProgress("Started processing file: " + files[i + j]);
                        }
                        catch (Exception e)
                        {
                            DataReporter.ReportProgress(string.Format(@"Failed to start process with arguments: {0}
    Error details: {1}", psi.Arguments, e.ToString()));
                            success = false;
                        }
                    }
                    while (running.Any(x => !x.HasExited))
                    {
                        Thread.Sleep(100);
                    }
                    foreach (var item in running)
                    {
                        if (item.ExitCode != 0)
                        {
                            DataReporter.ReportProgress(
                                string.Format("Parser process returned an error {0}, arguments: {1}",
                                item.ExitCode, item.StartInfo.Arguments));
                            success = false;
                        }
                    }
                    DataReporter.ReportProgress("Finished processing of the last group of files.");
                }
            }
            else
            {
                for (int i = 0; i < files.Length; i++)
                {
                    psi.Arguments = string.Format(args, files[i], Path.GetFileName(NameModifier(files[i])));
                    psi.WorkingDirectory = Path.GetDirectoryName(files[i]);
                    Process proc;
                    try
                    {
                        proc = Process.Start(psi);
                        while (!proc.HasExited)
                        {
                            Thread.Sleep(100);
                        }
                        if (proc.ExitCode != 0)
                        {
                            DataReporter.ReportProgress(
                                string.Format("Parser process returned an error {0}, arguments: {1}",
                                proc.ExitCode, proc.StartInfo.Arguments));
                            success = false;
                        }
                        DataReporter.ReportProgress("Finished processing file: " + files[i]);
                    }
                    catch (Exception e)
                    {
                        DataReporter.ReportProgress(string.Format(@"Failed to start process with arguments: {0}
Error details: {1}", psi.Arguments, e.ToString()));
                        success = false;
                    }
                }
            }
            return success;
        }
        private static string NameModifier(string original)
        {
            return original.Replace(Path.GetFileName(original),
                string.Format(NameModificationFormat, Path.GetFileNameWithoutExtension(original), ReportsExtension));
        }
    }
}

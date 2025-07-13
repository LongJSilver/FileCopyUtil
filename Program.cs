using DotBox.Data;
using DotBox.Utils;
using Linea.Args;
using Linea.Utils;
using Some.Restraint;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace FileCopyUtil
{
    class Program
    {

        /* Research   Phase 2
		 * --
		 * Select 1 entry with Absolute Path                                     > <arg> 
		 * Select 1 entry from base directory using Name                         > <arg>
		 * Select n entries from current directory using Name-Regex              > <arg> -x
		 * Select n entries recursively from base directory using Name-Regex     > <arg> -x -rs
		 * Select n entries recursively from base directory using Path-Regex     > <arg> -px -rs
		 * * * * * * * * * * */

        /*
		 * arg: absolute path, or relative path, or name, or regex 
		 * base: dir to use as starting point. Defaults to environment current dir
		 *
		 * -x|nx:                 argument is name-regex; each entry's name should be checked against it
		 * -px:                   argument is path-regex; each entry's path should be checked against it
		 * -rs|r|RecursiveSearch: search recursively from base
		 * -rc|RecursiveCopy:     copy recursively from selected dirs
		 * -f:                    only select files
		 * -d:                    only select dirs
		 * -fd|df :               select files and dirs (default)
		 * * * * * * * * * */
        static void Main(string[] args)
        {
            ArgumentDescriptorCollection expected = new ArgumentDescriptorCollection();
            expected.AddSimpleValue(("argument", "arg", "absolute path, or relative path, or name, or regex"), ArgumentValueType.String, ArgumentOptions.Mandatory);
            expected.AddNamedValue(("BaseDirectory", "base", "dir to use as starting point. Defaults to environment current dir"), ArgumentValueType.FileSystemPath);
            expected.AddSimpleValue("SourceRoot", ArgumentValueType.String);
            expected.AddSimpleValue("DestinationRoot", ArgumentValueType.String, ArgumentOptions.Mandatory);
            expected.AddFlag(("NameRegex", "x", "nx", "argument is name-regex; each entry's name should be checked against it"));
            expected.AddFlag(("PathRegex", "px", "argument is path-regex; each entry's path should be checked against it"));
            expected.AddFlag(("rs", "RecursiveSelection", "r", "search recursively from base"));
            expected.AddFlag(("rc", "RecursiveCopy", "copy recursively from selected dirs"));
            expected.AddFlag(("FileOnly", "SelectFileOnly", "f", "File", "file", "only select files"));
            expected.AddFlag(("DirectoryOnly", "SelectDirectoryOnly", "d", "DirOnly", "Dir", "dir", "only select dirs"));
            expected.AddFlag(("fd", "df", "SelectAll", "All", "select files and dirs (default)"));
            expected.AddFlag(("e", "ex", "explain", "Print a description of the arguments received, for debugging purposes"));
            expected.AddFlag(("debug", "Launch the default debugger"));
            expected.AddFlag(("watch", "w", "Create a watch for modified files in the 'base' directory and copy them in the target ('argument') directory"));

            expected.AddConstraint(ConstraintType.OneOrLess, "f", "d", "fd");

            string commands = string.Empty;
            var splitargs = Environment.GetCommandLineArgs();
            if (splitargs.Length > 1)
            {
                var ExecutableNameLen = splitargs[0].Length;
                if (Environment.CommandLine.StartsWith("\""))
                    ExecutableNameLen += 2;
                commands = Environment.CommandLine.Substring(ExecutableNameLen).Trim();
            }

            ParsedArguments p = ParsedArguments.ProcessArguments(commands, expected);

            if (p.HasFlag("debug"))
                Debugger.Launch();

            bool exit = false;
            if (p.HasFlag("e"))
            {
                Console.Error.WriteLine();

                if (p.Count <= 1)
                {
                    Console.Error.WriteLine("************************");
                    Console.Error.WriteLine("[No arguments received]");
                    Console.Error.WriteLine("************************");
                }
                else
                {

                    IEnumerable<string> explanations = p.GetArgumentExplanations().RowsToFixedLengthStrings(separator: " | ", maxColumnLen: 24);
                    int explLen = explanations.First().Length;
                    Console.Error.WriteLine(new string('*', explLen));
                    foreach (string item in explanations)
                    {
                        Console.Error.WriteLine(item);
                    }
                    Console.Error.WriteLine(new string('*', explLen));
                }

            }

            if (p.HasErrors)
            {
                exit = true;


                p.GetErrorsDescriptions().ForEach(s => Console.Error.WriteLine(s));
                Console.Error.WriteLine(expected.CreateUsageString(Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0])));
            }



            if (exit)
                return;
            //-----------
            string baseDir, UserArg;
            bool isRecursiveSelection = p.HasFlag("r");
            bool isRecursiveCopy = p.HasFlag("r");

            bool selectFiles, selectDirs;
            if (!(p.HasFlag("f") || p.HasFlag("d")) //nessuno dei due
                || (p.HasFlag("f") && p.HasFlag("d"))  //entrambi
              )
            {
                //in entrambi i casi selezioniamo tutto
                selectFiles = selectDirs = true;
            }
            else
            {
                selectFiles = p.HasFlag("f");
                selectDirs = p.HasFlag("d");
            }

            bool IsPathRegex = false;
            Regex userRegex = null;
            ParsedArgument arg;

            UserArg = p["arg"].Value;

            string sourceRoot;
            if (p.HasArgument("SourceRoot"))
                sourceRoot = p["SourceRoot"];
            else sourceRoot = Environment.CurrentDirectory;
            if (!Directory.Exists(sourceRoot))
            {
                sourceRoot = Path.Combine(Environment.CurrentDirectory, sourceRoot);
            }
            sourceRoot = Path.GetFullPath(sourceRoot);
            if (p.HasArgument("base", out arg))
            {
                baseDir = arg.Value;
            }
            else baseDir = sourceRoot;
            baseDir = baseDir.TrimEnd(Path.DirectorySeparatorChar);

            if (p.HasFlag("watch"))
            {
                SetupFileWatch(UserArg, sourceRoot, p["destinationRoot"]);
                return;
            }

            if (p.HasFlag("e"))
            {
                Console.Error.WriteLine("Base Directory: {0}", baseDir);
            }
            if (p.HasFlag("x"))
            {
                userRegex = new Regex(UserArg);
                IsPathRegex = false;
            }
            else if (p.HasFlag("px"))
            {
                userRegex = new Regex(UserArg);
                IsPathRegex = true;
            }
            else
            {
                //not a regex, let's clean it before use
                UserArg = UserArg.TrimEnd(Path.DirectorySeparatorChar);
            }

            List<string> allFoundEntries = new List<string>();

            if (userRegex != null)
            {
                bool EntryMatches(string entry)
                {
                    string ToMatch = IsPathRegex ? entry : Path.GetFileName(entry);
                    return userRegex.IsMatch(ToMatch);
                }
                ;

                if (isRecursiveSelection)
                {
                    // Select n entries recursively from base directory using Name-Regex     > <arg> -x -rs
                    // Select n entries recursively from base directory using Path-Regex     > <arg> -px -rs
                    FileSys.NavigateFileSystem(baseDir, shouldExploreThisDirectory: _ => true,
                            shouldConsiderThisDirectory: dir => selectDirs && EntryMatches(dir),
                            shouldConsiderThisFile: file => selectFiles && EntryMatches(file),
                            OnFileFound: entry => allFoundEntries.Add(entry),
                            OnDirectoryFound: entry => allFoundEntries.Add(entry)
                            );
                }
                else
                {
                    // Select n entries from current directory using Name-Regex              > <arg> -x
                    Directory.EnumerateFileSystemEntries(baseDir).ForEach(
                        entry =>
                        {
                            if (((selectFiles && File.Exists(entry)) || (selectDirs && Directory.Exists(entry))) && EntryMatches(entry))
                                allFoundEntries.Add(entry);
                        });
                }

            }
            else
            {
                //normal search
                if (isRecursiveSelection)
                {
                    FileSys.LoopAllDirectoriesRecursively(baseDir, _ => true, dir =>
                    {
                        string combined = Path.Combine(dir, UserArg);
                        if ((selectFiles && File.Exists(combined)) || (selectDirs && Directory.Exists(combined)))
                            allFoundEntries.Add(combined);
                    });
                }
                else
                {

                    if ((selectFiles && File.Exists(UserArg)) || (selectDirs && Directory.Exists(UserArg)))
                    {
                        //absolute file or folder path
                        allFoundEntries.Add(UserArg);
                    }
                    else
                    {
                        //maybe relative?
                        string combined = Path.Combine(baseDir, UserArg);
                        if ((selectFiles && File.Exists(combined)) || (selectDirs && Directory.Exists(combined)))
                            allFoundEntries.Add(combined);
                    }
                }

            }

            FileSys.CopyFromRootPath(allFoundEntries, sourceRoot, p["destinationRoot"], CopyDirectoryContents: isRecursiveCopy);
        }

        private static void SetupFileWatch(string fileRegex, string source, string destination)
        {
            bool abort = false;
            if (!Directory.Exists(source))
            {
                Console.Error.WriteLine("The watch source is invalid!");
                abort = true;
            }
            if (!Directory.Exists(destination))
            {
                Console.Error.WriteLine("The watch source is invalid!");
                abort = true;
            }
            if (abort) return;


            HashSet<string> toCopy = new HashSet<string>();

            void ActualCopy()
            {
                lock (toCopy)
                {

                    while (toCopy.Count > 0)
                    {
                        string s = toCopy.First();
                        int retries = 0;
                        Console.Error.Write($"Copying : {s} ...");
                        while (true)
                        {
                            try
                            {
                                FileSys.CopyFromRootPath(s, source, destination, CopyDirectoryContents: false, ShouldOverride: _ => true);
                                Console.Error.WriteLine($"Done [{DateTime.Now:H:mm:ss}]");
                                break;
                            }
                            catch (Exception e)
                            {
                                retries++;
                                System.Threading.Thread.Sleep(1000);
                                if (retries % 5 == 0)
                                    Console.Error.Write($".");
                            }
                        }
                        toCopy.Remove(s);

                    }
                }
            }

            Regex r = new Regex(fileRegex);
            Defer d = Defer.UsingTask()
                     .ForAtLeast(500).ToExecute(ActualCopy)
                     .Named("DeferredCopy").Build();

            void BufferAction(string s)
            {
                if (r.IsMatch(s))
                {
                    lock (toCopy)
                    {

                        toCopy.Add(s);
                        d.Trigger();
                    }
                }
            }
            CreateFileWatcher(source, BufferAction);
            Console.Error.WriteLine("Created file watch from ");
            Console.Error.WriteLine($"       {source}");
            Console.Error.WriteLine("to ");
            Console.Error.WriteLine($"       {destination}");

            object dummyMonitor = new object();
            while (true)
            {
                lock(dummyMonitor)
                {
                    Monitor.Wait(dummyMonitor, 1000);
                }
            }
        }
        public static void CreateFileWatcher(string path, Action<string> deleg)
        {
            // Add event handlers.
            FileSystemEventHandler f = new FileSystemEventHandler((object source, FileSystemEventArgs e) => deleg(e.FullPath));
            RenamedEventHandler f2 = new RenamedEventHandler((object source, RenamedEventArgs e) => deleg(e.FullPath));

            // Create a new FileSystemWatcher and set its properties.
            FileSystemWatcher watcher = new FileSystemWatcher
            {
                Path = path,
                /* Watch for changes in LastAccess and LastWrite times, and 
				   the renaming of files or directories. */
                NotifyFilter = NotifyFilters.LastAccess | NotifyFilters.LastWrite
               | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                IncludeSubdirectories = true
            };

            watcher.Changed += f;
            watcher.Created += f;
            watcher.Deleted += f;
            watcher.Renamed += f2;

            // Begin watching.
            watcher.EnableRaisingEvents = true;
        }

    }
}

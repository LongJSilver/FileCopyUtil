using Linea.Args;
using Linea.Utils;
using Some.Restraint;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Diagnostics.SymbolStore;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace FileCopyUtil
{
    public static class Program
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
            expected.LoadFromXML(typeof(Program).Assembly.GetManifestResourceStream("FileCopyUtil.Arguments.xml"));

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
            bool shouldExplain = p.HasFlag("e");

            if (shouldExplain)
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

            if (p.HasFlag("h"))
            {
                exit = true;
                Stream source = typeof(Program).Assembly.GetManifestResourceStream("FileCopyUtil.help.txt");
                TextWriter destination = Console.Out;

                if (source != null)
                {
                    using (StreamReader reader = new StreamReader(source))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            destination.WriteLine(line);
                        }
                    }
                }
                else
                {
                    Console.Error.WriteLine("Help file not found.");
                }

            }

            if (!exit && p.HasErrors)
            {
                exit = true;


                p.GetErrorsDescriptions().ForEach(s => Console.Error.WriteLine(s));
                Console.Error.WriteLine(expected.CreateUsageString(Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0])));
            }

            if (exit)
                return;

            //-----------
            string baseDir, UserArg;
            bool isRecursiveSelection = p.HasFlag("r") || p.HasFlag("rs");
            bool isRecursiveCopy = p.HasFlag("r") || p.HasFlag("rc");

            bool flatten = p.HasFlag("flatten");

            bool selectFiles, selectDirs;

            if (!(p.HasFlag("f") || p.HasFlag("d") || p.HasFlag("fd")))
            {
                selectFiles = selectDirs = true;
            }
            else
            {
                selectFiles = p.HasFlag("f") || p.HasFlag("fd");
                selectDirs = p.HasFlag("d") || p.HasFlag("fd");
            }

            bool IsPathRegex = false;
            Regex userRegex = null;
            ParsedArgument arg;

            UserArg = p["arg"].Value;


            if (p.HasArgument("base", out arg))
            {
                baseDir = arg.Value;
            }
            else baseDir = Environment.CurrentDirectory;

            baseDir = baseDir.TrimEnd(Path.DirectorySeparatorChar);

            if (shouldExplain)
            {
                Console.Error.WriteLine("Base Directory: {0}", baseDir);
            }

            string sourceRoot;
            if (p.HasArgument("SourceRoot"))
                sourceRoot = p["SourceRoot"];
            else sourceRoot = baseDir;


            if (!Directory.Exists(sourceRoot))
            {
                sourceRoot = Path.Combine(Environment.CurrentDirectory, sourceRoot);
            }
            sourceRoot = Path.GetFullPath(sourceRoot);

            bool verbose = p.HasFlag("v");

            if (p.HasFlag("watch"))
            {
                Options opt = Options.None;
                if (flatten) opt |= Options.Flatten;
                if (verbose) opt |= Options.Verbose;
                if (isRecursiveCopy) opt |= Options.RecursiveCopy;
                if (isRecursiveSelection) opt |= Options.RecursiveSelection;
                if (selectFiles) opt |= Options.CopyFiles;
                if (selectDirs) opt |= Options.CopyDirectories;


                SetupFileWatch(UserArg, sourceRoot, p["destinationRoot"], opt);
                return;
            }


            if (p.HasFlag("nx"))
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

            HashSet<string> allFoundEntries = new HashSet<string>();
            bool EntryMatches(string entry)
            {
                if (userRegex != null)
                {
                    string ToMatch = IsPathRegex ? entry : Path.GetFileName(entry);
                    return userRegex.IsMatch(ToMatch);
                }
                else
                {
                    return true;
                }
            }
                ;
            if (userRegex != null)
            {
                if (isRecursiveSelection)
                {
                    // Select n entries recursively from base directory using Name-Regex     > <arg> -x -rs
                    // Select n entries recursively from base directory using Path-Regex     > <arg> -px -rs
                    NavigateFileSystem(baseDir, 
                        shouldExploreThisDirectory: dir  => 
                        !isRecursiveCopy //if the copy process is not recursive, we should check if the contents of the folder should be included separately
                     || !allFoundEntries.Contains(dir)/* if the copy process IS recursive and the folder is already in the copy list, 
                                                       * all content will already be included and we don't need further exploration */,                        
                        
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
                    LoopAllDirectoriesRecursively(baseDir, _ => true, dir =>
                    {
                        string combined = Path.Combine(dir, UserArg);
                        if ((selectFiles && File.Exists(combined)) || (selectDirs && Directory.Exists(combined)))
                            allFoundEntries.Add(combined);
                    });
                }
                else
                {
                    string ToCopy = UserArg;
                    if (!Path.IsPathRooted(ToCopy))
                    {
                        ToCopy = Path.Combine(baseDir, ToCopy);
                    }

                    if ((selectFiles && File.Exists(ToCopy)) || (selectDirs && Directory.Exists(ToCopy)))
                    {
                        //absolute file or folder path
                        allFoundEntries.Add(ToCopy);
                    }
                    else
                    {
                        //maybe relative?
                        string combined = Path.Combine(baseDir, ToCopy);
                        if ((selectFiles && File.Exists(combined)) || (selectDirs && Directory.Exists(combined)))
                            allFoundEntries.Add(combined);
                    }
                }

            }

            Action<string, string> OnCopy = null;
            if (verbose) OnCopy = ReportCopy;

            if (flatten)
            {
                CopyFlattened(allFoundEntries, p["destinationRoot"], CopyDirectoryContents: isRecursiveCopy, OnCopied: OnCopy);
            }
            else
            {
                CopyFromRootPath(allFoundEntries, sourceRoot, p["destinationRoot"], CopyDirectoryContents: isRecursiveCopy, OnCopied: OnCopy);
            }
        }
        private static void ReportCopy(string from, string to)
        {
            Console.WriteLine("Copied : {0} => {1}", from, to);
        }
        [Flags]
        enum Options
        {
            None = 0,
            CopyFiles = 1,
            CopyDirectories = 2,
            RecursiveCopy = 4,
            RecursiveSelection = 8,
            Flatten = 16,
            Verbose = 32,
            Recursive = RecursiveSelection | RecursiveCopy,
            CopyAll = CopyFiles | CopyDirectories

        }

        private static void SetupFileWatch(string fileRegex, string source, string destination, Options opt)
        {
            bool recursiveCopy = opt.HasFlag(Options.RecursiveCopy);
            bool recursiveSelection = opt.HasFlag(Options.RecursiveSelection);
            bool flatten = opt.HasFlag(Options.Flatten);
            bool verbose = opt.HasFlag(Options.Verbose);
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
                        toCopy.Remove(s);

                        if (File.Exists(s) && !opt.HasFlag(Options.CopyFiles))
                        {
                            continue;
                        }

                        if (Directory.Exists(s) && !opt.HasFlag(Options.CopyDirectories))
                        {
                            continue;
                        }

                        int retries = 0;
                        if (verbose) Console.Error.Write($"Copying : {s} ...");
                        while (true)
                        {
                            try
                            {
                                if (flatten)
                                {
                                    CopyFlattened(s, destination, CopyDirectoryContents: recursiveCopy, OnCopied: null);
                                }
                                else
                                {
                                    CopyFromRootPath(s, source, destination, CopyDirectoryContents: recursiveCopy, ShouldOverride: _ => true);
                                }
                                if (verbose) Console.Error.WriteLine($"Done [{DateTime.Now:H:mm:ss}]");
                                break;
                            }
                            catch (Exception e)
                            {
                                retries++;
                                System.Threading.Thread.Sleep(1000);
                                if (verbose && retries % 5 == 0)
                                    Console.Error.Write($".");
                            }
                        }

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
            CreateFileWatcher(source, recursiveSelection, BufferAction);
            Console.Error.WriteLine("Created file watch from ");
            Console.Error.WriteLine($"       {source}");
            Console.Error.WriteLine("to ");
            Console.Error.WriteLine($"       {destination}");

            object dummyMonitor = new object();
            while (true)
            {
                lock (dummyMonitor)
                {
                    Monitor.Wait(dummyMonitor, 1000);
                }
            }
        }
        public static void CreateFileWatcher(string path, bool recursive, Action<string> deleg)
        {
            // Add event handlers.
            FileSystemEventHandler f = new FileSystemEventHandler((object source, FileSystemEventArgs e) =>
            {

                switch (e.ChangeType)
                {
                    case WatcherChangeTypes.Created:
                    case WatcherChangeTypes.Renamed:
                    case WatcherChangeTypes.Changed:
                        deleg(e.FullPath);
                        break;
                    case WatcherChangeTypes.All:
                    case WatcherChangeTypes.Deleted:
                        break;
                }
            }
                );
            RenamedEventHandler f2 = new RenamedEventHandler((object source, RenamedEventArgs e) => deleg(e.FullPath));

            // Create a new FileSystemWatcher and set its properties.
            FileSystemWatcher watcher = new FileSystemWatcher
            {
                Path = path,
                /* Watch for changes in LastAccess and LastWrite times, and 
				   the renaming of files or directories. */
                NotifyFilter = NotifyFilters.LastAccess | NotifyFilters.LastWrite
               | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                IncludeSubdirectories = recursive
            };

            watcher.Changed += f;
            watcher.Created += f;
            watcher.Deleted += f;
            watcher.Renamed += f2;

            // Begin watching.
            watcher.EnableRaisingEvents = true;
        }

        public static void LoopAllDirectoriesRecursively(string rootFolder, Func<string, bool> shouldConsiderThisDir, Action<String> OnDirFound) =>
             NavigateFileSystem(
                            rootFolder,
                            _ => true, //explore all
                            shouldConsiderThisDir, OnDirFound,
                            _ => false, _ => { } //ignore files
                            );

        public static bool CopyFlattened(string entryToCopy_absolutePath, string destination, bool CopyDirectoryContents = true, Action<string, string> OnCopied = null)
        {
            bool IsDirectory;
            string name;
            if (Directory.Exists(entryToCopy_absolutePath))
            {
                IsDirectory = true;
                name = Path.GetFileName(entryToCopy_absolutePath);
            }
            else if (File.Exists(entryToCopy_absolutePath))
            {
                IsDirectory = false;
                name = Path.GetFileName(entryToCopy_absolutePath);

            }
            else
            {
                //this entry does not exist, nothing to do
                return false;
            }

            string newPath = Path.Combine(destination, name);

            if (IsDirectory)
            {
                Directory.CreateDirectory(newPath);
                OnCopied?.Invoke(entryToCopy_absolutePath, newPath);
                if (CopyDirectoryContents)
                {
                    //The elements inside the folder must not be flattened
                    bool result = true;
                    NavigateFileSystem(entryToCopy_absolutePath, _ => true, s =>
                       result &= CopyFromRootPath(s, entryToCopy_absolutePath, newPath, CopyDirectoryContents: CopyDirectoryContents, OnCopied: OnCopied)
                    );
                    return result;
                }
                else
                {
                    return true;
                }
            }
            else
            {
                Directory.CreateDirectory(Directory.GetParent(newPath).FullName);
                if (File.Exists(newPath))
                {
                    File.Delete(newPath);
                }
                File.Copy(entryToCopy_absolutePath, newPath);
                OnCopied?.Invoke(entryToCopy_absolutePath, newPath);
                return true;
            }
        }
        public static bool CopyFlattened(IEnumerable<string> entriesToCopy, string destinationRoot, bool CopyDirectoryContents = true, Action<string, string> OnCopied = null)
        {
            bool result = true;
            foreach (var entryToCopy_absolutePath in entriesToCopy)
            {
                result &= CopyFlattened(entryToCopy_absolutePath, destinationRoot, CopyDirectoryContents, OnCopied: OnCopied);
            }
            return result;
        }

        public static bool CopyFromRootPath(IEnumerable<string> entriesToCopy, string sourceRoot, string destinationRoot, bool CopyDirectoryContents = true, Action<string, string> OnCopied = null)
        {
            bool success = true;
            foreach (var item in entriesToCopy)
            {
                bool copied = CopyFromRootPath(item, sourceRoot, destinationRoot, CopyDirectoryContents, OnCopied: OnCopied);
                success &= copied;
            }
            return success;
        }


        public static bool CopyFromRootPath(string entryToCopy_absolutePath, string sourceRoot, string destinationRoot,
                                            bool CopyDirectoryContents = true, Func<string, bool> ShouldOverride = null,
                                            Action<string, string> OnCopied = null)
        {
            bool IsDirectory;
            if (Directory.Exists(entryToCopy_absolutePath))
            {
                IsDirectory = true;

            }
            else if (File.Exists(entryToCopy_absolutePath))
            {
                IsDirectory = false;
            }
            else
            {
                //this entry does not exist, nothing to do
                return false;
            }
            sourceRoot = sourceRoot.TrimEnd(Path.DirectorySeparatorChar);
            destinationRoot = destinationRoot.TrimEnd(Path.DirectorySeparatorChar);

            string destinationSubPath;
            if (entryToCopy_absolutePath.StartsWith(sourceRoot, StringComparison.InvariantCultureIgnoreCase))
            {
                destinationSubPath = entryToCopy_absolutePath.Substring(sourceRoot.Length);
            }
            else
            {
                //not part of the source root
                return false;
            }

            destinationSubPath = destinationSubPath.TrimStart(Path.DirectorySeparatorChar);
            string newPath = Path.Combine(destinationRoot, destinationSubPath);

            if (IsDirectory)
            {
                Directory.CreateDirectory(newPath);
                OnCopied?.Invoke(entryToCopy_absolutePath, newPath);
                if (CopyDirectoryContents)
                {
                    bool result = true;
                    NavigateFileSystem(entryToCopy_absolutePath, _ => true, s =>
                       result &= CopyFromRootPath(s, sourceRoot, destinationRoot, CopyDirectoryContents: false, OnCopied: OnCopied)
                    //The "copydirectorycontents" flag must only apply to the first folder!
                    );
                    return result;
                }
                else
                {
                    return true;
                }
            }
            else
            {
                Directory.CreateDirectory(Directory.GetParent(newPath).FullName);
                if (File.Exists(newPath))
                {
                    if (ShouldOverride?.Invoke(newPath) ?? false)
                        File.Delete(newPath);
                    else
                    {
                        return false;
                    }
                }
                File.Copy(entryToCopy_absolutePath, newPath);
                OnCopied?.Invoke(entryToCopy_absolutePath, newPath);
                return true;
            }
        }


        public static void NavigateFileSystem(string startFrom, Func<string, bool> shouldConsiderThisElement, Action<String> OnElementFound) =>
     NavigateFileSystem(startFrom,
                        shouldExploreThisDirectory: s => true,
                        shouldConsiderThisDirectory: shouldConsiderThisElement,
                        OnDirectoryFound: OnElementFound,
                        shouldConsiderThisFile: shouldConsiderThisElement,
                        OnFileFound: OnElementFound);

        public static void NavigateFileSystem(
    string startFrom,
    Func<string, bool> shouldExploreThisDirectory,
    Func<string, bool> shouldConsiderThisDirectory,
    Action<String> OnDirectoryFound,
    Func<string, bool> shouldConsiderThisFile,
    Action<String> OnFileFound)
        {
            IEnumerable<String> files;
            try
            {
                files = Directory.EnumerateFiles(startFrom);
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
            foreach (string file in files)
            {
                if (shouldConsiderThisFile(file))
                    OnFileFound(file);
            }
            IEnumerable<String> dirs = Directory.EnumerateDirectories(startFrom);
            foreach (string directory in dirs)
            {
                if (shouldConsiderThisDirectory(directory))
                    OnDirectoryFound(directory);
                if (shouldExploreThisDirectory(directory))
                    NavigateFileSystem(directory, shouldExploreThisDirectory, shouldConsiderThisDirectory, OnDirectoryFound, shouldConsiderThisFile, OnFileFound);
            }
        }

        public static void ForEach<EnumerableType>
            (this IEnumerable<EnumerableType> list, Action<EnumerableType> action)
        {
            foreach (EnumerableType item in list)
            {
                action(item);
            }
        }
        public static IEnumerable<ResultType> ForEach<EnumerableType, ResultType>(
              this IEnumerable<EnumerableType> list,
              Func<EnumerableType, ResultType> function)
        {
            List<ResultType> _result = new List<ResultType>();
            list.ForEach(l => _result.Add(function(l)));
            return _result;
        }

    }

}

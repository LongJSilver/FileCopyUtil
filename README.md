# FileCopyUtil

**FileCopyUtil** is a command-line utility for advanced file and directory copying, designed for flexible selection, filtering, and automation. It supports both one-time copy operations and a "watch" mode for real-time synchronization.

---

## Features

- **Granular Selection:**  
  - Select files, directories, or both.
  - Filter entries by name or using regular expressions.
  - Recursive or non-recursive selection.

- **Flexible Copying:**  
  - Copy selected files and/or directories, with options to preserve or flatten directory structure.
  - Recursive copy of directory contents is configurable and independent from selection recursion.
  - Optionally define a custom source root to control how subdirectories are replicated in the destination.

- **Watch Mode:**  
  - Monitor a directory for changes and automatically copy new or modified files to the destination in real time.

- **Detailed Control via Arguments:**  
  - All features are accessible and configurable through command-line arguments and flags.

---

## Usage

### Basic Syntax

```
FCU <argument> <destination> [options]
```

### Arguments


| Argument Name      | Aliases           | Description                                                                                  |
|--------------------|-------------------|----------------------------------------------------------------------------------------------|
| argument           | arg               |                                                                                              |
| destination        | dest              | The target directory for copied files                                                        |
| SourceRoot         | srcRoot           | Optional: define a subfolder as the logical source root for structure replication.           |
| BaseDirectory      | base              | Set the starting directory for the selection phase (defaults to current directory).          |

All arguments can be specified by name or anonymously, in which case the software will attempt to assign each received value to its argument:

```
FCU "Foo.bar" "c:\Users\Alice\In Wonderland"  "d:\destination" 
```
will be treated as 

```
FCU -argument="Foo.bar" -DestinationRoot="c:\Users\Alice\In Wonderland"  -SourceRoot"d:\destination" 
```
Note that "d:\destination" is interpreted as the SourceRoot because the software gives priority to the mandatory arguments, and thus assigns the first string to `argument`, the second to `DestinationRoot` and any following un-named argument to the remaining optional arguments in the order they appear in the table above.

### Flags

| Flag Name          | Aliases           | Description                                                                                  |
|--------------------|-------------------|----------------------------------------------------------------------------------------------|
| NameRegex          | nx                | Treat `<argument>` as a name regex.                                                          |
| PathRegex          | px                | Treat `<argument>` as a path regex.                                                          |
| RecursiveSelection | rs                | Enable recursive selection.                                                                  |
| RecursiveCopy      | rc                | Enable recursive copy of selected directories.                                               |
| Recursive          | r                 | Shortcut for enabling both recursive selection and copy.                                     |
| FileOnly           | file, f           | Select only files.                                                                           |
| DirectoryOnly      | DirOnly, dir, d   | Select only directories.                                                                     |
| SelectAll          | df, fd, all,      | Select both files and directories (default).                                                 |
| flatten            | flat, fl          | Copy all selected files directly into the destination root, flattening the hierarchy.        |
| explain            | ex, e             | Prints a small explanation of the received inputs, useful for troubleshooting                |
| debug              |                   | Attempts to launch the default debugger before operation                                     |
| watch              | w                 | Enable watch mode for real-time copying.                                                     |
| verbose            | v                 | Verbose output: print each copied file.                                                      |
| help               | h                 | Prints a short guide and exi                                                                 |


All names and aliases are case insensitive; keep in mind that while you are free to use any alias for brevity or clarity the tool will always use the main name when printing explanations and debug messages.

## Details

- **Selection Phase Flags:**
  - `-FileOnly` : Only include files during the selection phase. Note that when -RecursiveSelection is enabled this will not prevent the search to explore all the subdirectories, it will only prevent them from entering the copy list directly. 
  - `-DirOnly` : Only include directories in the copy list during the selection  phase.
  - `-SelectAll`: Include everything; note that this is flag is included only for clarity, because its presence yields the same result as not specifying any selection flag.

	**These flags are mutually exclusive, specifying more than one will result in error.**
	
- **Filtering:**  
  - No flag: `<argument>` is treated as a plain name.
  - `-NameRegex`: `<argument>` is a regex to be applied to the name only.
  - `-PathRegex`: `<argument>` is a regex to be applied to the full path.

	**These flags are mutually exclusive, specifying more than one will result in error.**

- **Recursion:**  
  - `-rs`: Recursive selection.
  - `-rc`: Recursive copy of selected directories (if any). If a directory clears all the filters and gets included within the copy list, this option will force the copy of all its contents even if they had been excluded during the selection phase. On the other hand this will also prevent the contents of these directories from being flattened (when that option is enabled) because they are not being copied as part of the original selection, and are instead handled blindly as part of the folder itself. 

- **Flattening:**  
  - `-flatten`: All selected files are copied directly into the destination root without preserving the original directory structure. This does NOT apply to the content of directories which are copied recursively when `-rc` is specified.

- **Debugging and Verbosity:**  
  - `-explain`: Prints a description of the received arguments; useful to see how the tool interpreted the received input.
  - `-debug`: Attempts to launch the debugger before running.
  - `-help`: Prints a short guide ad exits.
  - `-verbose`: Verbose output; this prints a line for every copied entry.


### Example Commands

- **Copy all `.txt` files recursively, keeping the original folder structure:**
```
FCU ".*.txt$" -nx -rs -f -destinationRoot="C:\targetFolder"
```

- **Copy a specific directory:**
```
FCU MyFolder -d -destinationRoot="C:\targetFolder"
```

- **Watch for new `.log` files and copy them without the original folder structure:**
```
FCU ".*.log$" -nx -watch -rs -f -flat -destinationRoot="C:\targetFolder"
```


- **Copy with custom source root (replicates only substructure below `SourceRoot`):**
```
FCU ".+.xml$" -BaseDirectory="D:\src\a\b" -SourceRoot="D:\src\" -DestinationRoot="C:\target" -nx
```
| Origin File                      | Without SourceRoot                |  With SourceRoot                      |
|--------------------------------- | --------------------------------- | ------------------------------------- |
| **D:\src\a\b**\Child1\config.xml | **C:\target**\Child1\config.xml   | **C:\target**\a\b\Child1\config.xml   |
| **D:\src\a\b**\app.xml           | **C:\target**\app.xml             | **C:\target**\a\b\app.xml             |



#

## Notes

- The tool is designed for .NET Framework 4.8.
- In watch mode (-watch), the process runs until manually stopped (Ctrl+C).


## License

Apache 2.0
# CsPurity

## Usage

To run the application run the following from the repo's root directory, where `<path/to/dir>` is the path to a C# project whose files you want to analyze.

```sh
CsPurity --project CsPurity/ <path/to/dir>
```

Or if you want to pass only the paths to the file(s) to analyze, use the flag `--files`

```sh
CsPurity --project CsPurity/ --files <path/to/file1>  <path/to/file2> ...
```

If you want pass the content of one file as a string to the program, for instance by piping, use can use the `--string` flag.

For more information, see the help:

```sh
CsPurity --project /d/Melker/git-repos/masters-thesis/CsPurity/ --help
```

## Development

To run tests, run `dotnet test CsPurity/` from the repo's root directory.

## Dependencies

- [`dotnet`](https://dotnet.microsoft.com/)

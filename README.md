# Master's thesis code

## Dependencies

- `dotnet`

## Usage

To run the application run the following from the repo's root directory, where `*path/to/dir*` is the path to a C# project who's files you want to analyze.

```sh
dotnet run --project CsPurity/ *path/to/dir*
```

Or if you want to pass only the paths to the file(s) to analyze, use the flag `--files`

```sh
dotnet run --project CsPurity/ --files *path/to/file1*  *path/to/file2* ...
```

If you want pass the content of one file as a string to the program, for instance by piping, use can use the `--string` flag.

## Development

To run tests run `dotnet test CsPurity/` from the repo's root directory.

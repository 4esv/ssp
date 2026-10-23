using Ssp.DocsGen;

// Usage: Ssp.DocsGen <repo-root> <output-dir>
if (args.Length != 2)
{
    Console.Error.WriteLine("usage: Ssp.DocsGen <repo-root> <output-dir>");
    return 2;
}
var pages = DocsGenerator.Generate(args[0], args[1]);
Console.WriteLine($"Ssp.DocsGen: wrote {pages.Count} pages to {args[1]}");
return 0;

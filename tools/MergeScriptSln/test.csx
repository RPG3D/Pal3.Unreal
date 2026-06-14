#r "nuget: Microsoft.Build, 17.12.6"
var sln = Microsoft.Build.Construction.SolutionFile.Parse(@"E:\UEProject\SharpDemo\SharpDemo.sln");
foreach (var p in sln.ProjectsInOrder.Take(2))
{
    Console.WriteLine($"Project: {p.ProjectName}");
    var t = p.GetType();
    foreach (var prop in t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        Console.WriteLine($"  {prop.Name}: {prop.GetValue(p)}");
    foreach (var field in t.GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
        Console.WriteLine($"  [field] {field.Name}");
}

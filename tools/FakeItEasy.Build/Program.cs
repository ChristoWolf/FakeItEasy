using System.Text;
using System.Xml.Linq;
using SimpleExec;
using static Bullseye.Targets;
using static SimpleExec.Command;

Project[] projectsToPack =
{
    "src/FakeItEasy/FakeItEasy.csproj",
    "src/FakeItEasy.Extensions.ValueTask/FakeItEasy.Extensions.ValueTask.csproj"
};

var testSuites = new Dictionary<string, string[]>
{
    ["unit"] = new[]
    {
        "tests/FakeItEasy.Tests"
    },
    ["integ"] = new[]
    {
        "tests/FakeItEasy.IntegrationTests",
        "tests/FakeItEasy.IntegrationTests.VB",
    },
    ["spec"] = new[]
    {
        "tests/FakeItEasy.Specs"
    },
    ["approve"] = new[]
    {
        "tests/FakeItEasy.Tests.Approval"
    }
};

Target("default", DependsOn("unit", "integ", "spec", "approve", "pack"));

Target(
    "build",
    () => Run("dotnet", "build FakeItEasy.sln -c Release /maxcpucount /nr:false /verbosity:minimal /nologo /bl:artifacts/logs/build.binlog"));

foreach (var testSuite in testSuites)
{
    Target(
        testSuite.Key,
        DependsOn("build"),
        forEach: testSuite.Value,
        action: testDirectory => Run("dotnet", "test --configuration Release --no-build --nologo -- RunConfiguration.NoAutoReporters=true", testDirectory));
}

Target(
    "pack",
    DependsOn("build"),
    forEach: projectsToPack,
    action: project => Run("dotnet", $"pack {project.Path} --configuration Release --no-build --nologo --output {Path.GetFullPath("artifacts/output")}"));

Target(
    "force-approve",
    () =>
    {
        foreach (var received in Directory.EnumerateFiles("tests/FakeItEasy.Tests.Approval/ApprovedApi", "*.received.txt", SearchOption.AllDirectories))
        {
            File.Copy(received, received.Replace(".received.txt", ".approved.txt", StringComparison.OrdinalIgnoreCase), overwrite: true);
        }
    });

Target(
    "initialize-user-properties",
    () =>
    {
        if (!File.Exists("FakeItEasy.user.props"))
        {
            var defaultUserProps = @"
<Project>
<PropertyGroup>
<BuildProfile></BuildProfile>
</PropertyGroup>
</Project>".Trim();
            File.WriteAllText("FakeItEasy.user.props", defaultUserProps, Encoding.UTF8);
        }
    });

foreach (var profile in Directory.EnumerateFiles("profiles", "*.props").Select(Path.GetFileNameWithoutExtension))
{
    Target(
        "use-profile-" + profile,
        DependsOn("initialize-user-properties"),
        () =>
        {
            var xmlDoc = XDocument.Load("FakeItEasy.user.props");

            var buildProfileElement = xmlDoc.Root!.Elements("PropertyGroup").Elements("BuildProfile").FirstOrDefault();
            if (buildProfileElement is null)
            {
                var propertyGroupElement = xmlDoc.Root.Element("PropertyGroup");
                if (propertyGroupElement is null)
                {
                    propertyGroupElement = new XElement("PropertyGroup");
                    xmlDoc.Root.Add(propertyGroupElement);
                }

                buildProfileElement = new XElement("BuildProfile");
                propertyGroupElement.Add(buildProfileElement);
            }

            if (buildProfileElement.Value != profile)
            {
                buildProfileElement.Value = profile!;
                xmlDoc.Save("FakeItEasy.user.props");
            }
        });
}

RunTargetsAndExit(args, messageOnly: ex => ex is NonZeroExitCodeException);

record Project(string Path)
{
    public static implicit operator Project(string path) => new Project(path);

    public override string ToString() => this.Path.Split('/').Last();
}

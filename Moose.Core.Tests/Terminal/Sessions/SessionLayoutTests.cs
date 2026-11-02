using Moose.Core.Terminal.Sessions;

namespace Moose.Core.Tests.Terminal.Sessions;

public sealed class SessionLayoutTests : IDisposable
{
    readonly string directory = Directory.CreateTempSubdirectory("moose-sessions-").FullName;

    [Fact]
    public void Parse_ReadsTabsAndResolvesDirectoriesAgainstHomeAndTheFile()
    {
        var layout = SessionLayout.Parse("""
            active = 1

            [[tabs]]
            title = "server"
            working_directory = "~/project"
            program = "fish"
            args = ["-c", "dotnet watch"]

            [[tabs]]
            working_directory = "docs"
            """, home: "/home/moose", baseDirectory: "/srv/layouts");

        Assert.Equal(1, layout.Active);
        Assert.Equal(new TabLayout("server", "fish", null, "/home/moose/project"), layout.Tabs[0] with { Args = null });
        Assert.Equal(["-c", "dotnet watch"], layout.Tabs[0].Args!);
        Assert.Equal(new TabLayout(WorkingDirectory: "/srv/layouts/docs"), layout.Tabs[1]);
    }

    [Fact]
    public void Parse_ClampsActiveToTheTabs()
    {
        Assert.Equal(0, SessionLayout.Parse("active = 5\n[[tabs]]\n").Active);
        Assert.Empty(SessionLayout.Parse("").Tabs);
    }

    [Fact]
    public void Parse_RejectsInvalidToml()
    {
        Assert.Throws<FormatException>(() => SessionLayout.Parse("[[tabs]\n"));
    }

    [Fact]
    public void Save_WritesALayoutThatLoadsBackTheSame()
    {
        var path = Path.Combine(directory, "nested", "layout.toml");
        var layout = new SessionLayout([new TabLayout("logs", "tail", ["-f", "a \"b\".log"], "/var/log"), new TabLayout(WorkingDirectory: "/tmp")], 1);

        layout.Save(path);
        var loaded = SessionLayout.Load(path);

        Assert.Equal(layout.Active, loaded.Active);
        Assert.Equal(layout.Tabs[0].Args, loaded.Tabs[0].Args);
        Assert.Equal(layout.Tabs[0] with { Args = null }, loaded.Tabs[0] with { Args = null });
        Assert.Equal(layout.Tabs[1], loaded.Tabs[1]);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Store_FindsNamedSessionsAndFiles()
    {
        var store = new SessionStore(new EnvironmentOptions(directory, null, Path.Combine(directory, "config"), Path.Combine(directory, "state")));
        var layout = new SessionLayout([new TabLayout(WorkingDirectory: "/tmp")]);

        var saved = store.Save("work/main", layout);
        store.SaveLast(layout);

        Assert.Equal(Path.Combine(directory, "config", "moose", "sessions", "work_main.toml"), saved);
        Assert.Equal(["work_main"], store.Names());
        Assert.Equal(layout.Tabs, store.Load("work_main").Tabs);
        Assert.Equal(layout.Tabs, store.Load(saved).Tabs);
        Assert.Equal(layout.Tabs, store.LoadLast()!.Tabs);
        Assert.Equal(Path.Combine(directory, "state", "moose", "session.toml"), store.LastSessionPath);
        Assert.Throws<FileNotFoundException>(() => store.Load("missing"));
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}

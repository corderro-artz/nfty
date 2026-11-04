using System;
using System.Collections.Generic;
using System.IO;
using Nfty.App.Services;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The Linux config-folder rule: where state goes when nothing can be written beside the executable
/// (an install under <c>/opt</c> or <c>/usr</c>, an AppImage's read-only mount). The environment is
/// injected and every folder is a temp one, so no test here reads or writes the developer's real
/// <c>$HOME</c> or <c>~/.config</c>.
/// </summary>
public class UserConfigFolderTests
{
    private static Func<string, string?> Env(params (string Key, string? Value)[] vars)
    {
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (k, v) in vars) map[k] = v;
        return k => map.TryGetValue(k, out var v) ? v : null;
    }

    [Fact]
    public void Xdg_config_home_wins_when_it_is_absolute()
    {
        Assert.Equal("/x/cfg/nfty",
            UserConfigFolder.Resolve(true, Env(("XDG_CONFIG_HOME", "/x/cfg/"), ("HOME", "/home/a"))));
    }

    [Fact]
    public void A_relative_or_empty_xdg_config_home_is_ignored_and_home_dot_config_is_used()
    {
        Assert.Equal("/home/a/.config/nfty",
            UserConfigFolder.Resolve(true, Env(("XDG_CONFIG_HOME", "rel/cfg"), ("HOME", "/home/a"))));
        Assert.Equal("/home/a/.config/nfty",
            UserConfigFolder.Resolve(true, Env(("XDG_CONFIG_HOME", ""), ("HOME", "/home/a"))));
        Assert.Equal("/home/a/.config/nfty", UserConfigFolder.Resolve(true, Env(("HOME", "/home/a"))));
    }

    [Fact]
    public void No_home_means_no_rule()
    {
        Assert.Null(UserConfigFolder.Resolve(true, Env()));
    }

    [Fact]
    public void Off_linux_there_is_no_config_folder_rule()
    {
        // Windows keeps its order exactly: beside the executable, then the working directory.
        Assert.Null(UserConfigFolder.Resolve(false, Env(("XDG_CONFIG_HOME", "/x"), ("HOME", "/home/a"))));
    }

    [Fact]
    public void An_unwritable_executable_folder_sends_state_to_the_config_folder_before_the_working_directory()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var beside = Directory.CreateDirectory(Path.Combine(root, "opt")).FullName;
            File.WriteAllText(Path.Combine(beside, StateStore.FolderName), "a file in the way");
            var working = Directory.CreateDirectory(Path.Combine(root, "cwd")).FullName;
            var config = Path.Combine(root, "config", UserConfigFolder.Name);

            var store = new StateStore(kitchen: null, beside: beside, working: working, userConfig: config);
            store.Write("recents.json", "[]");

            Assert.Equal(StoreLocation.UserConfig, store.Resolution.Location);
            Assert.Equal(Path.GetFullPath(config), store.Resolution.Directory);
            Assert.Equal("[]", File.ReadAllText(Path.Combine(config, "recents.json")));
            Assert.False(Directory.Exists(Path.Combine(working, StateStore.FolderName)));
            Assert.Contains("config folder", store.Resolution.Description, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_writable_executable_folder_still_wins_so_a_portable_copy_keeps_its_state_beside_itself()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var beside = Directory.CreateDirectory(Path.Combine(root, "app")).FullName;
            var config = Path.Combine(root, "config", UserConfigFolder.Name);

            var store = new StateStore(kitchen: null, beside: beside, working: root, userConfig: config);

            Assert.Equal(StoreLocation.BesideExecutable, store.Resolution.Location);
            Assert.False(Directory.Exists(config));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void An_unusable_config_folder_falls_through_to_the_working_directory()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var beside = Directory.CreateDirectory(Path.Combine(root, "opt")).FullName;
            File.WriteAllText(Path.Combine(beside, StateStore.FolderName), "in the way");
            var config = Path.Combine(root, "nfty");
            File.WriteAllText(config, "in the way too");
            var working = Directory.CreateDirectory(Path.Combine(root, "cwd")).FullName;

            var store = new StateStore(kitchen: null, beside: beside, working: working, userConfig: config);

            Assert.Equal(StoreLocation.WorkingDirectory, store.Resolution.Location);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_store_built_without_a_config_folder_never_has_the_rule()
    {
        // The default is null, so a test (or anything else) constructing the store by hand cannot
        // reach the real ~/.config by omission.
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var beside = Directory.CreateDirectory(Path.Combine(root, "opt")).FullName;
            File.WriteAllText(Path.Combine(beside, StateStore.FolderName), "in the way");
            var store = new StateStore(kitchen: null, beside: beside, working: root);
            Assert.Equal(StoreLocation.WorkingDirectory, store.Resolution.Location);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}

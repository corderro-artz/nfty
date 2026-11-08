using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Nfty.App.Services;
using Xunit;

namespace Nfty.App.Tests;

public class AppSharedServicesAuditTests
{
    [Fact]
    public void Pascal_case_settings_updates_survive_reload_for_every_owner()
    {
        var store = StateStore.InMemory();
        store.Write(SettingsJson.FileName, """
            {"Theme":"dark","DefaultCanvas":64,"PinningProvider":"pinata","KuboApi":"http://localhost:5001",
             "Threads":2,"future":{"nested":[1,true,null,"keep"]}}
            """);
        var general = new AppSettingsService(store);
        var publishing = new PublishingSettingsService(store, new InMemorySecretStore());

        general.SetTheme(ThemeChoice.Light);
        general.SetDefaultCanvasSide(128);
        publishing.SetProvider(PinningProvider.LocalIpfs);
        publishing.SetKuboApi("http://localhost:9000");
        new ParallelismSettingService(store, () => null).Set(1);

        var reloaded = new AppSettingsService(store);
        var reloadedPublishing = new PublishingSettingsService(store, new InMemorySecretStore());
        Assert.Equal(ThemeChoice.Light, reloaded.Theme);
        Assert.Equal(128, reloaded.DefaultCanvasSide);
        Assert.Equal(PinningProvider.LocalIpfs, reloadedPublishing.Provider);
        Assert.Equal("http://localhost:9000", reloadedPublishing.KuboApi);
        Assert.Equal(1, new ParallelismSettingService(store, () => null).Saved);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"nested":[1,true,null,"keep"]}"""),
            SettingsJson.Read(store)["future"]));
    }

    [Fact]
    public void Setting_a_key_removes_all_case_aliases_and_preserves_other_keys()
    {
        var store = StateStore.InMemory();
        store.Write(SettingsJson.FileName, """
            {"Theme":"dark","theme":"system","THEME":null,"theMe":123,
             "DefaultCanvas":64,"Future":{"arr":[1,{"Theme":"nested"}]},"future":false}
            """);
        var before = SettingsJson.Read(store);

        SettingsJson.Set(store, "theme", "light");

        var after = SettingsJson.Read(store);
        Assert.Equal(["theme"], after.Select(p => p.Key)
            .Where(k => string.Equals(k, "theme", StringComparison.OrdinalIgnoreCase)).ToArray());
        Assert.Equal(ThemeChoice.Light, new AppSettingsService(store).Theme);
        foreach (var key in new[] { "DefaultCanvas", "Future", "future" })
            Assert.True(JsonNode.DeepEquals(before[key], after[key]));
    }

    [Fact]
    public void Resetting_a_setting_writes_one_canonical_null_and_keeps_unknown_values()
    {
        var store = StateStore.InMemory();
        store.Write(SettingsJson.FileName, """{"Threads":2,"THREADS":4,"unknown":17}""");

        SettingsJson.Set(store, "threads", null);

        Assert.Null(new ParallelismSettingService(store, () => null).Saved);
        var doc = SettingsJson.Read(store);
        Assert.Equal(["unknown", "threads"], doc.Select(p => p.Key).ToArray());
        Assert.Null(doc["threads"]);
        Assert.Equal(17, doc["unknown"]!.GetValue<int>());
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(0, "synthetic-warning")]
    [InlineData(1, "")]
    public void Successful_or_absent_secret_deletion_clears_the_saved_hint(int code, string error)
    {
        var secrets = new SecretToolStore((args, stdin) =>
        {
            Assert.Null(stdin);
            return args[0] == "lookup" ? (0, "synthetic-key-abcd", "") : (code, "", error);
        });
        var settings = new PublishingSettingsService(StateStore.InMemory(), secrets);
        Assert.Equal("••••abcd", settings.SavedKeyHint);
        int changes = 0;
        settings.Changed += () => changes++;

        settings.ForgetPinataKey();

        Assert.Null(settings.SavedKeyHint);
        Assert.Equal(1, changes);
    }

    [Theory]
    [InlineData(1, "synthetic-sensitive-diagnostic")]
    [InlineData(1, "\n")]
    [InlineData(2, "")]
    [InlineData(2, "synthetic-sensitive-diagnostic")]
    public void Failed_secret_deletion_keeps_the_saved_hint_and_sanitizes_the_error(int code, string error)
    {
        var secrets = new SecretToolStore((args, stdin) =>
            args[0] == "lookup" ? (0, "synthetic-key-abcd", "") : (code, "", error));
        var settings = new PublishingSettingsService(StateStore.InMemory(), secrets);
        Assert.Equal("••••abcd", settings.SavedKeyHint);
        int changes = 0;
        settings.Changed += () => changes++;

        var failure = Assert.Throws<InvalidOperationException>(settings.ForgetPinataKey);

        Assert.Contains("forget", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("synthetic-sensitive-diagnostic", failure.ToString(), StringComparison.Ordinal);
        Assert.Equal("••••abcd", settings.SavedKeyHint);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Secret_tool_keeps_secret_input_out_of_attributes_and_uses_the_same_target()
    {
        var calls = new List<(string[] Args, string? Input)>();
        var secrets = new SecretToolStore((args, stdin) =>
        {
            calls.Add((args, stdin));
            return (0, "synthetic-key-ü", "");
        });

        secrets.Write("nfty/pinata", "synthetic-key-ü");
        Assert.Equal("synthetic-key-ü", secrets.Read("nfty/pinata"));
        secrets.Delete("nfty/pinata");

        Assert.Equal(["store", "--label=nfty nfty/pinata", "service", "nfty", "account", "nfty/pinata"], calls[0].Args);
        Assert.Equal("synthetic-key-ü", calls[0].Input);
        Assert.Equal(["lookup", "service", "nfty", "account", "nfty/pinata"], calls[1].Args);
        Assert.Equal(["clear", "service", "nfty", "account", "nfty/pinata"], calls[2].Args);
        Assert.Null(calls[1].Input);
        Assert.Null(calls[2].Input);
        Assert.All(calls, call => Assert.DoesNotContain("synthetic-key-ü", call.Args));
    }

    [Fact]
    public void Existing_mac_key_is_updated_without_attempting_add()
    {
        string stored = "old-key";
        MacKeychainStore.Upsert(() => { stored = "new-key"; return 0; },
            () => throw new InvalidOperationException("Add must not replace an existing key."));
        Assert.Equal("new-key", stored);
    }

    [Theory]
    [InlineData(-25293)]
    [InlineData(-50)]
    public void Failed_mac_update_does_not_attempt_add_or_change_the_previous_key(int status)
    {
        string stored = "old-key";
        var failure = Assert.Throws<InvalidOperationException>(() => MacKeychainStore.Upsert(() => status,
            () => { stored = "wrong-key"; return 0; }));
        Assert.Contains(status.ToString(System.Globalization.CultureInfo.InvariantCulture), failure.Message, StringComparison.Ordinal);
        Assert.Equal("old-key", stored);
    }

    [Fact]
    public void Absent_mac_key_is_added_after_update_reports_not_found()
    {
        string? stored = null;
        bool updated = false;
        MacKeychainStore.Upsert(() => { updated = true; return -25300; }, () =>
        {
            Assert.True(updated);
            stored = "new-key";
            return 0;
        });
        Assert.Equal("new-key", stored);
    }

    [Theory]
    [InlineData(-25299)]
    [InlineData(-25293)]
    public void Failed_mac_add_reports_its_status(int status)
    {
        var failure = Assert.Throws<InvalidOperationException>(() => MacKeychainStore.Upsert(() => -25300, () => status));
        Assert.Contains(status.ToString(System.Globalization.CultureInfo.InvariantCulture), failure.Message, StringComparison.Ordinal);
    }
}

namespace SwitchToDestination;

using System;
using System.Linq;
using HarmonyLib;
using JetBrains.Annotations;
using Railloader;
using Serilog;
using UI.Builder;

[UsedImplicitly]
public sealed class SwitchToDestinationPlugin : SingletonPluginBase<SwitchToDestinationPlugin>, IModTabHandler
{

    public static IModdingContext Context { get; private set; } = null!;
    public static IUIHelper UiHelper { get; private set; } = null!;
    public static Settings Settings { get; private set; }

    private readonly ILogger _Logger = Log.ForContext<SwitchToDestinationPlugin>()!;

    public SwitchToDestinationPlugin(IModdingContext context, IUIHelper uiHelper)
    {
        Context = context;
        UiHelper = uiHelper;

        Settings = Context.LoadSettingsData<Settings>("SwitchToDestination") ?? new Settings();
    }

    public override void OnEnable()
    {
        _Logger.Information("OnEnable");
        var harmony = new Harmony("SwitchToDestination");
        harmony.PatchAll();
    }

    public override void OnDisable()
    {
        _Logger.Information("OnDisable");
        var harmony = new Harmony("SwitchToDestination");
        harmony.UnpatchAll();
    }

    public void ModTabDidOpen(UIPanelBuilder builder)
    {
        builder.AddField("Send debug logs to console", builder.AddToggle(() => Settings.EnableDebug, o => Settings.EnableDebug = o)!);
    }

    public void ModTabDidClose()
    {
        Context.SaveSettingsData("SwitchToDestination", Settings);
    }
}
﻿using Dalamud;
using Dalamud.Game;
using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.Game.Gui.Toast;
using Dalamud.Interface.Windowing;
using Dalamud.Logging;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;

namespace HarpHero
{
    public class Plugin : IDalamudPlugin
    {
        public string Name => "Harp Hero";

        private readonly DalamudPluginInterface pluginInterface;
        private readonly CommandManager commandManager;
        private readonly Framework framework;

        private readonly WindowSystem windowSystem = new("HarpHero");

        private readonly PluginWindowStatus statusWindow;
        private readonly CommandInfo statusCommand;

        private readonly UIReaderBardPerformance uiReaderPerformance;
        private readonly UnsafeReaderPerformanceKeybinds keybindReader;
        private readonly TrackAssistant trackAssistant;
        private readonly UnsafeMetronomeLink metronome;
        private readonly NoteUIMapper noteUiMapper;
        private readonly Localization locManager;

        public static Localization CurrentLocManager;
        private string[] supportedLangCodes = { "en" };

        private List<ITickable> tickableStuff = new List<ITickable>();
        private Configuration configuration { get; init; }

        public Plugin(DalamudPluginInterface pluginInterface, Framework framework, CommandManager commandManager, GameGui gameGui, SigScanner sigScanner, ToastGui toastGui)
        {
            this.pluginInterface = pluginInterface;
            this.commandManager = commandManager;
            this.framework = framework;

            configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            configuration.Initialize(pluginInterface);

            // prep data scrapers
            uiReaderPerformance = new UIReaderBardPerformance(gameGui);
            keybindReader = new UnsafeReaderPerformanceKeybinds(gameGui, sigScanner);
            metronome = new UnsafeMetronomeLink(gameGui, sigScanner);

            // prep utils
            locManager = new Localization("assets/loc", "", true);
            locManager.SetupWithLangCode(pluginInterface.UiLanguage);
            CurrentLocManager = locManager;

            noteUiMapper = new NoteUIMapper();
            var noteInputMapper = new NoteInputMapper(noteUiMapper, keybindReader);

            trackAssistant = new TrackAssistant(uiReaderPerformance, metronome, configuration);
            tickableStuff.Add(trackAssistant);

            var fileManager = new MidiFileManager();
            fileManager.OnImported += (_) => { trackAssistant.OnTracksImported(fileManager.tracks); };

            // prep UI
            statusWindow = new PluginWindowStatus(trackAssistant, fileManager, configuration);
            var trackViewWindow = new PluginWindowTrackView(trackAssistant);
            var noteAssistantWindow = new PluginWindowNoteAssistant(uiReaderPerformance, trackAssistant, noteUiMapper, noteInputMapper);
            var bindAssistantWindow = new PluginWindowBindAssistant(uiReaderPerformance, trackAssistant, noteUiMapper, noteInputMapper);
            var noteAssistant2Window = new PluginWindowNoteAssistant2(uiReaderPerformance, trackAssistant, noteUiMapper, configuration);
            var scoreWindow = new PluginWindowScore(uiReaderPerformance, trackAssistant, configuration);

            statusWindow.OnShowTrack += (track) => trackViewWindow.OnShowTrack(track);
            uiReaderPerformance.OnVisibilityChanged += (active) => statusWindow.IsOpen = active;
            trackAssistant.OnTrackChanged += (valid) => noteUiMapper.OnTrackChanged(trackAssistant);
            trackAssistant.OnPlayChanged += (active) => noteInputMapper.OnPlayChanged(active);
            trackAssistant.OnPerformanceScore += (accuracy) =>
            {
                if (configuration.ShowScore)
                {
                    var accuracyToastOptions = new QuestToastOptions() { Position = QuestToastPosition.Centre, DisplayCheckmark = true, IconId = 0, PlaySound = true };
                    toastGui.ShowQuest(string.Format(Localization.Localize("Toast_PerformanceAccuracy", "Accuracy: {0:P0}"), accuracy), accuracyToastOptions);
                }
            };

            windowSystem.AddWindow(statusWindow);
            windowSystem.AddWindow(trackViewWindow);
            windowSystem.AddWindow(noteAssistantWindow); tickableStuff.Add(noteAssistantWindow);
            windowSystem.AddWindow(bindAssistantWindow); tickableStuff.Add(bindAssistantWindow);
            windowSystem.AddWindow(noteAssistant2Window); tickableStuff.Add(noteAssistant2Window);
            windowSystem.AddWindow(scoreWindow);

            // prep plugin hooks
            statusCommand = new(OnCommand);
            commandManager.AddHandler("/harphero", statusCommand);

            pluginInterface.LanguageChanged += OnLanguageChanged;
            pluginInterface.UiBuilder.Draw += OnDraw;
            pluginInterface.UiBuilder.OpenConfigUi += OnOpenConfig;

            framework.Update += Framework_OnUpdateEvent;

            // keep at the end to update everything created here
            locManager.LocalizationChanged += (_) => CacheLocalization();
            CacheLocalization();
        }

        private void OnLanguageChanged(string langCode)
        {
            // check if resource is available, will cause exception if trying to load empty json
            if (Array.Find(supportedLangCodes, x => x == langCode) != null)
            {
                locManager.SetupWithLangCode(langCode);
            }
            else
            {
                locManager.SetupWithFallbacks();
            }
        }

        private void CacheLocalization()
        {
            statusCommand.HelpMessage = string.Format(Localization.Localize("Cmd_Status", "Show state of {0} plugin"), Name);
        }

        public void Dispose()
        {
            trackAssistant.Dispose();
            commandManager.RemoveHandler("/harphero");
            windowSystem.RemoveAllWindows();
            framework.Update -= Framework_OnUpdateEvent;
            pluginInterface.Dispose();
        }

        private void OnCommand(string command, string args)
        {
            statusWindow.IsOpen = true;
        }

        private void OnOpenConfig()
        {
            statusWindow.showConfigs = true;
            statusWindow.IsOpen = true;
        }

        private void OnDraw()
        {
            windowSystem.Draw();
        }

        private void Framework_OnUpdateEvent(Framework framework)
        {
            try
            {
                uiReaderPerformance.Update();
                noteUiMapper.Update(uiReaderPerformance.cachedState);
                metronome.Update();

                float deltaSeconds = (float)framework.UpdateDelta.TotalSeconds;
                foreach (var tickOb in tickableStuff)
                {
                    tickOb.Tick(deltaSeconds);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "state update failed");
            }
        }
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CncController.Models;

namespace CncController.ViewModels;

/// <summary>
/// ATC Axis Settings tab – configure the Carousel rotation servo
/// (EtherCAT slave mapping + velocity / acceleration).
/// Only shown for Umbrella (carousel) ATC type.
/// </summary>
public partial class AtcAxisSettingsViewModel : ObservableObject // [2026-03-06]
{
    // ── EtherCAT servo for carousel rotation ──

    [ObservableProperty]
    private DiscoveredSlave? selectedCarouselSlave; // [2026-03-06] null = not assigned

    [ObservableProperty]
    private double carouselMaxVel = 90.0; // deg/s [2026-03-06]

    [ObservableProperty]
    private double carouselMaxAccel = 360.0; // deg/s² [2026-03-06]

    [ObservableProperty]
    private int carouselPulsePerRev = 10000; // [2026-03-06] 編碼器脈衝/圈

    [ObservableProperty]
    private double carouselPitch = 360.0; // [2026-03-06] 每圈行程（旋轉軸=360度）

    // ── Available slaves from hardware scan ──

    public ObservableCollection<DiscoveredSlave> AvailableSlaves { get; } = new(); // [2026-03-06]

    // ── Methods ──

    /// <summary>
    /// Refresh the AvailableSlaves list from a hardware scan result.
    /// Currently accepts all slave types; may filter for servo-type later.
    /// </summary>
    public void UpdateSlaves(IEnumerable<DiscoveredSlave> slaves) // [2026-03-06]
    {
        AvailableSlaves.Clear();
        foreach (var s in slaves)
            AvailableSlaves.Add(s);
    }

    /// <summary>
    /// Populate this view-model from a persisted <see cref="AtcConfig"/>
    /// and the full list of discovered slaves.
    /// </summary>
    public void LoadFrom(AtcConfig config, IEnumerable<DiscoveredSlave> allSlaves) // [2026-03-06]
    {
        UpdateSlaves(allSlaves);

        SelectedCarouselSlave = config.CarouselSlaveIndex >= 0
            ? AvailableSlaves.FirstOrDefault(s => s.Index == config.CarouselSlaveIndex)
            : null;

        CarouselMaxVel      = config.CarouselMaxVel;
        CarouselMaxAccel    = config.CarouselMaxAccel;
        CarouselPulsePerRev = config.CarouselPulsePerRev; // [2026-03-06]
        CarouselPitch       = config.CarouselPitch;       // [2026-03-06]
    }

    /// <summary>
    /// Write the current settings back into an <see cref="AtcConfig"/>.
    /// </summary>
    public void SaveTo(AtcConfig config) // [2026-03-06]
    {
        config.CarouselSlaveIndex = SelectedCarouselSlave?.Index ?? -1;
        config.CarouselMaxVel       = CarouselMaxVel;
        config.CarouselMaxAccel     = CarouselMaxAccel;
        config.CarouselPulsePerRev  = CarouselPulsePerRev; // [2026-03-06]
        config.CarouselPitch        = CarouselPitch;       // [2026-03-06]
    }
}

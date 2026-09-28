using System;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// Time passing (decision "Next 43": month-by-month time with pause):
/// press play (or Space) and the months run at the chosen speed; each
/// January the year is played as a turn would be. Time pauses itself when
/// something needs you (a war, a succession, a revolt, a disaster). The
/// turn button still plays whole years at once.
/// </summary>
public partial class Hud
{
    [Signal] public delegate void PlayYearRequestedEventHandler();
    /// <summary>A month has passed on the clock: the armies march it.</summary>
    [Signal] public delegate void MonthPassedEventHandler();

    static readonly string[] MonthNames = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
    static readonly float[] Speeds = { 1f, 2f, 4f, 8f };   // months a second
    Button _playButton = null!, _speedButton = null!;
    bool _playing;
    int _speed = 1;
    double _monthClock;

    /// <summary>The month within the year (0 = January): the months the armies have marched this year.</summary>
    public int Month { get; private set; }

    /// <summary>After a month has been marched: the armies panel, reports and markers catch up.</summary>
    public void MonthPlayed()
    {
        RefreshArmiesPanel();
        CheckNewBattles();
        RefreshBattles();
        RefreshClock();
    }
    /// <summary>A year is being played (set by the game while it works).</summary>
    public bool TurnBusy { get; set; }

    Control BuildClock()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        _playButton = new Button { Text = "▶", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(52, 52),
            TooltipText = "Let the months run  (Space); time stops by itself when something needs you" };
        _playButton.Pressed += () => SetPlaying(!_playing);
        row.AddChild(_playButton);
        _speedButton = new Button { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(64, 52), TooltipText = "Speed: months a second" };
        _speedButton.Pressed += () =>
        {
            _speed = (_speed + 1) % Speeds.Length;
            RefreshClock();
        };
        row.AddChild(_speedButton);
        RefreshClock();
        return row;
    }

    public void SetPlaying(bool play)
    {
        _playing = play;
        _monthClock = 0;
        RefreshClock();
    }

    void RefreshClock()
    {
        _playButton.Text = _playing ? "❚❚" : "▶";
        _speedButton.Text = $"{Speeds[_speed]:0}×";
        if (_map != null)
            _dateLabel.Text = (Month > 0 || _playing ? MonthNames[Month] + " " : "") + ThemeAncient.YearText(_map.DemoYear);
    }

    void UpdateClock(double delta)
    {
        if (!_playing || TurnBusy)
            return;
        _monthClock += delta * Speeds[_speed];
        if (_monthClock < 1)
            return;
        _monthClock = 0;
        Month = Math.Min(11, _map.Game.MonthsMarched);   // in step with the armies (after a load too)
        if (Month < 11)
        {
            Month++;
            EmitSignal(SignalName.MonthPassed);
            RefreshClock();
            return;
        }
        Month = 0;
        EmitSignal(SignalName.PlayYearRequested);
    }

    /// <summary>After a year is played: back to January.</summary>
    public void YearPlayed()
    {
        Month = _map.Game.MonthsMarched;
        RefreshClock();
        CheckNewBattles();
        RefreshBattles();
    }
}

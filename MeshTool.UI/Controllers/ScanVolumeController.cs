using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MeshTool.Core.Config;
using MeshTool.UI.Models;

namespace MeshTool.UI.Controllers
{
    /// <summary>
    /// Manages scan volume settings and UI synchronization.
    /// Handles the relationship between scan volume settings and UI controls.
    /// </summary>
    public class ScanVolumeController
    {
        private readonly TextBox _txtCenterX;
        private readonly TextBox _txtCenterZ;
        private readonly TextBox _txtSizeX;
        private readonly TextBox _txtSizeZ;
        private readonly TextBox _txtYTop;
        private readonly TextBox _txtYBottom;
        private readonly TextBox _txtYaw;
        private readonly TextBox _txtRayTilt;
        private readonly Slider _sldDensity;
        private readonly Slider _sldFineDensity;
        private readonly TextBlock _txtBroadMeters;
        private readonly TextBlock _txtFineMeters;

        private bool _isSyncing;
        private float _finePhaseTargetStep = ScanDensity.DefaultFineStep;

        /// <summary>
        /// Gets or sets whether editing is enabled.
        /// </summary>
        public bool EditingEnabled { get; set; } = true;

        /// <summary>
        /// Gets the current fine phase target step.
        /// </summary>
        public float FinePhaseTargetStep => _finePhaseTargetStep;

        /// <summary>
        /// Raised when scan volume settings change.
        /// </summary>
        public event Action<ScanVolumeSettings>? ScanVolumeChanged;

        /// <summary>
        /// Raised when fine density changes.
        /// </summary>
        public event Action<float>? FineDensityChanged;

        /// <summary>
        /// Initializes a new instance of the ScanVolumeController.
        /// </summary>
        public ScanVolumeController(
            TextBox txtCenterX, TextBox txtCenterZ,
            TextBox txtSizeX, TextBox txtSizeZ,
            TextBox txtYTop, TextBox txtYBottom,
            TextBox txtYaw, TextBox txtRayTilt,
            Slider sldDensity, Slider sldFineDensity,
            TextBlock txtBroadMeters, TextBlock txtFineMeters)
        {
            _txtCenterX = txtCenterX;
            _txtCenterZ = txtCenterZ;
            _txtSizeX = txtSizeX;
            _txtSizeZ = txtSizeZ;
            _txtYTop = txtYTop;
            _txtYBottom = txtYBottom;
            _txtYaw = txtYaw;
            _txtRayTilt = txtRayTilt;
            _sldDensity = sldDensity;
            _sldFineDensity = sldFineDensity;
            _txtBroadMeters = txtBroadMeters;
            _txtFineMeters = txtFineMeters;

            AttachEventHandlers();
        }

        private void AttachEventHandlers()
        {
            _txtCenterX.TextChanged += OnTextChanged;
            _txtCenterZ.TextChanged += OnTextChanged;
            _txtSizeX.TextChanged += OnTextChanged;
            _txtSizeZ.TextChanged += OnTextChanged;
            _txtYTop.TextChanged += OnTextChanged;
            _txtYBottom.TextChanged += OnTextChanged;
            _txtYaw.TextChanged += OnTextChanged;
            _txtRayTilt.TextChanged += OnTextChanged;
            _sldDensity.ValueChanged += OnDensityChanged;
            _sldFineDensity.ValueChanged += OnFineDensityChanged;
        }

        private void OnTextChanged(object? sender, TextChangedEventArgs e)
        {
            if (!EditingEnabled || _isSyncing) return;
            if (TryReadSettingsFromUi(out var settings))
            {
                ScanVolumeChanged?.Invoke(settings);
            }
        }

        private void OnDensityChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (!EditingEnabled || _isSyncing) return;

            var current = ScanVolumeSettings.Default;
            if (TryReadSettingsFromUi(out var settings, logErrors: false))
            {
                current = settings;
            }

            var updated = current with { ProbeCellSize = DensityToCell(e.NewValue) };
            SyncToUi(updated);
            ScanVolumeChanged?.Invoke(updated);
        }

        private void OnFineDensityChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (!EditingEnabled || _isSyncing) return;

            _finePhaseTargetStep = DensityToFineStep(e.NewValue);
            UpdateMetersLabels(DensityToCell(_sldDensity.Value), _finePhaseTargetStep);
            FineDensityChanged?.Invoke(_finePhaseTargetStep);
        }

        /// <summary>
        /// Synchronizes the UI controls to match the given settings.
        /// </summary>
        public void SyncToUi(ScanVolumeSettings settings)
        {
            _isSyncing = true;
            try
            {
                _txtCenterX.Text = settings.CenterX.ToString("F1", CultureInfo.InvariantCulture);
                _txtCenterZ.Text = settings.CenterZ.ToString("F1", CultureInfo.InvariantCulture);
                _txtSizeX.Text = settings.SizeX.ToString("F1", CultureInfo.InvariantCulture);
                _txtSizeZ.Text = settings.SizeZ.ToString("F1", CultureInfo.InvariantCulture);
                _txtYTop.Text = settings.YTop.ToString("F1", CultureInfo.InvariantCulture);
                _txtYBottom.Text = settings.YBottom.ToString("F1", CultureInfo.InvariantCulture);
                _txtYaw.Text = settings.YawDegrees.ToString("F1", CultureInfo.InvariantCulture);
                _txtRayTilt.Text = settings.RayTiltDegrees.ToString("F1", CultureInfo.InvariantCulture);
                _sldDensity.Value = CellToDensity(settings.ProbeCellSize);
                _sldFineDensity.Value = FineStepToDensity(_finePhaseTargetStep);
                UpdateMetersLabels(settings.ProbeCellSize, _finePhaseTargetStep);
            }
            finally
            {
                _isSyncing = false;
            }
        }

        /// <summary>
        /// Attempts to read settings from the UI controls.
        /// </summary>
        public bool TryReadSettingsFromUi(out ScanVolumeSettings settings, bool logErrors = true)
        {
            settings = ScanVolumeSettings.Default;

            if (!TryParseFloat(_txtCenterX, out var cx) ||
                !TryParseFloat(_txtCenterZ, out var cz) ||
                !TryParseFloat(_txtSizeX, out var sx) ||
                !TryParseFloat(_txtSizeZ, out var sz) ||
                !TryParseFloat(_txtYTop, out var yTop) ||
                !TryParseFloat(_txtYBottom, out var yBottom) ||
                !TryParseFloat(_txtYaw, out var yaw) ||
                !TryParseFloat(_txtRayTilt, out var tilt))
            {
                settings = ScanVolumeSettings.Default;
                return false;
            }

            float probeCellSize = DensityToCell(_sldDensity.Value);
            settings = new ScanVolumeSettings(cx, cz, sx, sz, yTop, yBottom, yaw, tilt, probeCellSize).Sanitize();
            return true;
        }

        /// <summary>
        /// Gets the step size for scan handle dragging.
        /// </summary>
        public static float GetScanHandleStep(string key, bool fine, bool coarse)
        {
            float baseStep = key switch
            {
                "CenterX" or "CenterZ" => 1.0f,
                "SizeX" or "SizeZ" => 2.0f,
                "YTop" or "YBottom" => 1.0f,
                "Yaw" => 0.2f,
                "RayTilt" => 0.1f,
                "ProbeCell" => 0.25f,
                _ => 1.0f
            };

            if (fine) baseStep *= 0.1f;
            if (coarse) baseStep *= 10f;
            return baseStep;
        }

        /// <summary>
        /// Applies a delta to a scan volume setting.
        /// </summary>
        public ScanVolumeSettings ApplyDelta(ScanVolumeSettings settings, string key, float delta)
        {
            return key switch
            {
                "CenterX" => settings with { CenterX = settings.CenterX + delta },
                "CenterZ" => settings with { CenterZ = settings.CenterZ + delta },
                "SizeX" => settings with { SizeX = MathF.Max(10f, settings.SizeX + delta) },
                "SizeZ" => settings with { SizeZ = MathF.Max(10f, settings.SizeZ + delta) },
                "YTop" => settings with { YTop = settings.YTop + delta },
                "YBottom" => settings with { YBottom = settings.YBottom + delta },
                "Yaw" => settings with { YawDegrees = settings.YawDegrees + delta },
                "RayTilt" => settings with { RayTiltDegrees = settings.RayTiltDegrees + delta },
                "ProbeCell" => settings with { ProbeCellSize = MathF.Max(ScanDensity.MinProbeCell, settings.ProbeCellSize + delta) },
                _ => settings
            };
        }

        private void UpdateMetersLabels(float broadCellMeters, float fineStepMeters)
        {
            _txtBroadMeters.Text = $"{MathF.Round(broadCellMeters)} m";
            _txtFineMeters.Text = $"{MathF.Round(fineStepMeters)} m";
        }

        private static bool TryParseFloat(TextBox box, out float value)
        {
            return float.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static double CellToDensity(float cell)
        {
            float clamped = Math.Clamp(cell, ScanDensity.MinProbeCell, ScanDensity.MaxProbeCell);
            float t = (clamped - ScanDensity.MinProbeCell) / (ScanDensity.MaxProbeCell - ScanDensity.MinProbeCell);
            return 1.0 - t;
        }

        private static float DensityToCell(double density)
        {
            float d = (float)Math.Clamp(density, 0.0, 1.0);
            float t = 1.0f - d;
            return ScanDensity.MinProbeCell + ((ScanDensity.MaxProbeCell - ScanDensity.MinProbeCell) * t);
        }

        private static double FineStepToDensity(float step)
        {
            float clamped = Math.Clamp(step, ScanDensity.MinFineStep, ScanDensity.MaxFineStep);
            float t = (clamped - ScanDensity.MinFineStep) / (ScanDensity.MaxFineStep - ScanDensity.MinFineStep);
            return 1.0 - t;
        }

        private static float DensityToFineStep(double density)
        {
            float d = (float)Math.Clamp(density, 0.0, 1.0);
            float t = 1.0f - d;
            return ScanDensity.MinFineStep + ((ScanDensity.MaxFineStep - ScanDensity.MinFineStep) * t);
        }
    }
}

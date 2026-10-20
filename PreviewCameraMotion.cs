namespace FooxClothStudio;

public sealed class PreviewCameraMotion
{
    public double Yaw { get; private set; } = 25;
    public double Pitch { get; private set; } = 0;
    public double Distance { get; private set; } = 3.3;
    public double X { get; private set; }
    public double Y { get; private set; }
    private double _yaw = 25, _pitch = 0, _distance = 3.3, _x, _y;
    public bool Unlocked { get; private set; }
    public void SetUnlocked(bool unlocked)
    {
        Unlocked = unlocked;
        if (!unlocked) { _pitch = Pitch = 0; _x = X = 0; _y = Y = 0; }
    }
    public void Rotate(double dx, double dy) { _yaw += dx * .35; if (Unlocked) _pitch = Math.Clamp(_pitch + dy * .35, -85, 85); }
    public void Zoom(double delta) => _distance = Math.Clamp(_distance * Math.Exp(-delta / 120 * .12), .6, 12);
    public void Pan(double dx, double dy, double viewportHeight)
    {
        if (!Unlocked) return;
        var scale = 2 * _distance * Math.Tan(Math.PI / 8) / Math.Max(1, viewportHeight);
        _x -= dx * scale; _y += dy * scale;
    }
    public void Reset() { _yaw = Yaw = 25; _pitch = Pitch = 0; _distance = Distance = 3.3; _x = X = 0; _y = Y = 0; }
    public bool Advance(double seconds)
    {
        var alpha = 1 - Math.Exp(-Math.Clamp(seconds, 0, 1) / .055);
        static double Approach(double value, double target, double amount) => Math.Abs(target - value) < .0001 ? target : value + (target - value) * amount;
        Yaw = Approach(Yaw, _yaw, alpha); Pitch = Approach(Pitch, _pitch, alpha);
        Distance = Approach(Distance, _distance, alpha); X = Approach(X, _x, alpha); Y = Approach(Y, _y, alpha);
        return Yaw != _yaw || Pitch != _pitch || Distance != _distance || X != _x || Y != _y;
    }
}

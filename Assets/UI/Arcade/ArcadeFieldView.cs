using DirtAndDiamonds.Simulation.Arcade;
using Godot;

namespace DirtAndDiamonds.UI.Arcade;

/// <summary>
/// The 320x180 pixel surface of the G-0b prototype (arcade_baseball_overhaul.md
/// §10.1-§10.2): everything is drawn in code, placeholder-grade, from a
/// read-only <see cref="PrototypeSession"/>. It owns no gameplay state; the
/// only state here is the field camera's glide and a cosmetic RNG for shake.
/// That RNG is deliberately <see cref="System.Random"/>, never the sim's
/// RngState (presentation never touches the sim's streams).
///
/// Two cameras:
///  * Pitch view: behind home plate looking out, pitcher at the top, batter at
///    the bottom (the RBI framing). A pinhole projection of field metres gives
///    depth; a shadow under the ball carries its height.
///  * Hit view: on contact (after the hit-stop) a cut to an elevated oblique
///    view that glides after the ball to its landing. A smooth camera pan is
///    G-5's job; this is the "simple field view" the brief asks for.
///
/// Colors are placeholder palette, not the Phase 15 theme (the field is the one
/// pixel-art surface in the game, §10.0). The pitch-read cue uses the
/// Okabe-Ito colorblind-safe trio and varies trail length as a second cue.
/// Positions are rounded to whole pixels so nearest-neighbour scaling stays crisp.
/// </summary>
public sealed partial class ArcadeFieldView : Node2D
{
    public const int ViewWidth = 320;
    public const int ViewHeight = 180;

    // ---- Pitch camera (PLACEHOLDER constants): level, 3.2 m behind the plate's back point, 2.1 m up ----

    private const double CamY = -3.2;
    private const double CamZ = 2.1;
    private const double Focal = 150.0;
    private const float CenterX = 160f;
    private const float HorizonY = 52f;

    // ---- Hit camera (PLACEHOLDER): oblique, pixels per metre of ground, foreshortened depth, and height ----

    private const float HitScale = 2.2f;
    private const float HitDepthK = 0.62f;
    private const float HitHeightK = 0.9f;
    private const float HitFocusY = 140f;
    private const double CameraFollowRate = 5.0;
    private const double CameraMinY = 12.0;

    // ---- Bat pose, degrees, right-handed frame (0 = pointing across the plate, -90 = straight up) ----

    private const double CockedDeg = -105.0;
    private const double FollowThroughDeg = 70.0;

    // ---- Palette ----

    private static readonly Color Sky = new(0.30f, 0.38f, 0.52f);
    private static readonly Color Stands = new(0.16f, 0.18f, 0.26f);
    private static readonly Color Grass = new(0.18f, 0.46f, 0.24f);
    private static readonly Color GrassStripe = new(0.21f, 0.51f, 0.27f);
    private static readonly Color GrassInfield = new(0.20f, 0.49f, 0.26f);
    private static readonly Color Dirt = new(0.58f, 0.42f, 0.27f);
    private static readonly Color DirtMound = new(0.66f, 0.49f, 0.32f);
    private static readonly Color Chalk = new(0.92f, 0.92f, 0.90f);
    private static readonly Color BallColor = new(0.98f, 0.98f, 0.96f);
    private static readonly Color Shadow = new(0f, 0f, 0f, 0.38f);
    private static readonly Color Pants = new(0.78f, 0.78f, 0.80f);
    private static readonly Color Jersey = new(0.74f, 0.20f, 0.20f);
    private static readonly Color PitcherJersey = new(0.20f, 0.30f, 0.62f);
    private static readonly Color Skin = new(0.80f, 0.60f, 0.46f);
    private static readonly Color Helmet = new(0.12f, 0.16f, 0.30f);
    private static readonly Color BatColor = new(0.72f, 0.55f, 0.32f);
    private static readonly Color ZoneColor = new(0.92f, 0.92f, 0.90f, 0.55f);
    private static readonly Color FenceBase = new(0.10f, 0.14f, 0.22f);
    private static readonly Color FenceTop = new(0.94f, 0.89f, 0.26f);
    private static readonly Color Marker = new(0.84f, 0.37f, 0.00f);
    private static readonly Color CursorColor = new(0.94f, 0.89f, 0.26f);     // Okabe-Ito yellow
    private static readonly Color CursorPowerColor = new(0.84f, 0.37f, 0.00f); // vermillion
    private static readonly Color CursorLockedColor = new(0.80f, 0.80f, 0.80f, 0.7f);
    private static readonly Color CueFastball = new(0.90f, 0.62f, 0.00f);      // Okabe-Ito orange
    private static readonly Color CueBreaking = new(0.34f, 0.71f, 0.91f);      // sky blue
    private static readonly Color CueOffspeed = new(0.00f, 0.62f, 0.45f);      // bluish green

    private const int EllipseSegments = 20;
    private static readonly float[] UnitCos = BuildUnit(Math.Cos);
    private static readonly float[] UnitSin = BuildUnit(Math.Sin);

    // Scratch polygons, reused every frame.
    private readonly Vector2[] _quad = new Vector2[4];
    private readonly Vector2[] _pentagon = new Vector2[5];

    private PrototypeSession? _session;
    private double _camX;
    private double _camY = CameraMinY;
    private bool _wasHitView;

    public void Bind(PrototypeSession session) => _session = session;

    public override void _Process(double delta)
    {
        PrototypeSession? s = _session;
        if (s == null)
        {
            return;
        }

        bool hitView = s.InHitView;
        if (hitView && !_wasHitView)
        {
            _camX = 0.0;
            _camY = CameraMinY;
        }
        _wasHitView = hitView;

        if (hitView)
        {
            Vec3 ball = s.BallPosition;
            double k = 1.0 - Math.Exp(-delta * CameraFollowRate);
            _camX += (ball.X - _camX) * k;
            _camY += (Math.Max(ball.Y, CameraMinY) - _camY) * k;
        }

        // Cosmetic only, gated by reduce-motion inside ShakeActive.
        Position = s.ShakeActive
            ? new Vector2(Random.Shared.Next(-2, 3), Random.Shared.Next(-2, 3))
            : Vector2.Zero;
        QueueRedraw();
    }

    public override void _Draw()
    {
        PrototypeSession? s = _session;
        if (s == null)
        {
            DrawRect(new Rect2(0, 0, ViewWidth, ViewHeight), Grass);
            return;
        }

        if (s.InHitView)
        {
            DrawHitView(s);
        }
        else
        {
            DrawPitchView(s);
        }
    }

    // ------------------------------------------------------------------
    // Pitch view
    // ------------------------------------------------------------------

    private static Vector2 Pt(double x, double y, double z)
    {
        double depth = y - CamY;
        return new Vector2(
            (float)(CenterX + Focal * x / depth),
            (float)(HorizonY + Focal * (CamZ - z) / depth));
    }

    /// <summary>Pixels per metre at field depth <paramref name="y"/>.</summary>
    private static float PxPerMetre(double y) => (float)(Focal / (y - CamY));

    private static float Px(float v) => Mathf.Round(v);

    private void DrawPitchView(PrototypeSession s)
    {
        DrawRect(new Rect2(-8, -8, 336, 60), Sky);
        DrawRect(new Rect2(-8, 30, 336, 22), Stands);
        DrawRect(new Rect2(-8, HorizonY, 336, 140), Grass);

        // Dirt path from the plate area out to the mound.
        _quad[0] = Pt(-3.2, -1.0, 0.0);
        _quad[1] = Pt(3.2, -1.0, 0.0);
        _quad[2] = Pt(1.4, 18.6, 0.0);
        _quad[3] = Pt(-1.4, 18.6, 0.0);
        DrawColoredPolygon(_quad, Dirt);

        Vector2 mound = Pt(0.0, 18.44, 0.0);
        float moundW = Px(5.4f * PxPerMetre(18.44));
        DrawRect(new Rect2(Px(mound.X - moundW / 2f), Px(mound.Y - 2f), moundW, 4f), DirtMound);

        // Batter's boxes and the plate, on the ground.
        for (int side = -1; side <= 1; side += 2)
        {
            double xIn = 0.35 * side;
            double xOut = 1.25 * side;
            GroundLine(xIn, -0.45, xOut, -0.45);
            GroundLine(xOut, -0.45, xOut, 1.20);
            GroundLine(xOut, 1.20, xIn, 1.20);
            GroundLine(xIn, 1.20, xIn, -0.45);
        }
        double hw = ArcadeTuning.PlateHalfWidth;
        double front = ArcadeTuning.PlateFrontY;
        _pentagon[0] = Pt(-hw, front, 0.0);
        _pentagon[1] = Pt(hw, front, 0.0);
        _pentagon[2] = Pt(hw, front / 2.0, 0.0);
        _pentagon[3] = Pt(0.0, 0.0, 0.0);
        _pentagon[4] = Pt(-hw, front / 2.0, 0.0);
        DrawColoredPolygon(_pentagon, Chalk);

        DrawPitcher(s);
        DrawBatter(s);

        // The strike zone, at the plate-front plane where the umpire judges it.
        Vector2 zoneTopLeft = Pt(-hw, front, ArcadeTuning.ZoneTop);
        Vector2 zoneBottomRight = Pt(hw, front, ArcadeTuning.ZoneBottom);
        DrawRect(
            new Rect2(Px(zoneTopLeft.X), Px(zoneTopLeft.Y), Px(zoneBottomRight.X - zoneTopLeft.X), Px(zoneBottomRight.Y - zoneTopLeft.Y)),
            ZoneColor, false, 1f);

        DrawCursor(s);
        DrawPitchBall(s);
        DrawContactFlash(s);
    }

    private void GroundLine(double x0, double y0, double x1, double y1) =>
        DrawLine(Pt(x0, y0, 0.0), Pt(x1, y1, 0.0), Chalk, 1f);

    private void DrawPitcher(PrototypeSession s)
    {
        Vector2 feet = Pt(0.0, 18.44, 0.0);
        float px = Px(feet.X);
        float py = Px(feet.Y);
        bool righty = s.Settings.PitcherThrows == Hand.Right;
        float armDir = righty ? -1f : 1f;

        DrawRect(new Rect2(px - 1f, py - 5f, 3f, 5f), Pants);
        DrawRect(new Rect2(px - 2f, py - 9f, 5f, 4f), PitcherJersey);
        DrawRect(new Rect2(px - 1f, py - 12f, 3f, 3f), Skin);
        DrawRect(new Rect2(px - 1f, py - 13f, 3f, 1f), Helmet);

        Vec3 rp = ArcadeTuning.ReleasePoint(s.Settings.PitcherThrows);
        Vector2 release = Pt(rp.X, rp.Y, rp.Z);
        Vector2 shoulder = new(px + armDir * 2f, py - 8f);
        Vector2 hand;
        if (s.Phase == PrototypePhase.Windup)
        {
            Vector2 rest = new(px + armDir * 3f, py - 4f);
            Vector2 over = new(px + armDir, py - 17f);
            float w = (float)s.WindupProgress;
            hand = w < 0.5f ? rest.Lerp(over, w / 0.5f) : over.Lerp(release, (w - 0.5f) / 0.5f);
            DrawRect(new Rect2(Px(hand.X), Px(hand.Y), 1f, 1f), BallColor);
        }
        else
        {
            hand = release;
        }
        DrawLine(shoulder, hand, Skin, 1f);

        if (!s.Settings.ReduceMotion && s.Phase == PrototypePhase.Flight && s.FlightTime < 0.07)
        {
            DrawCircle(release, 2f, new Color(1f, 1f, 1f, 0.6f));
        }
    }

    private void DrawBatter(PrototypeSession s)
    {
        bool righty = s.Settings.BatterBats == Hand.Right;
        float dir = righty ? 1f : -1f;
        double bx = righty ? -0.85 : 0.85;
        Vector2 feet = Pt(bx, 0.0, 0.0);
        float sc = PxPerMetre(0.0);
        float fx = Px(feet.X);
        float fy = Px(feet.Y);

        float legH = Px(0.90f * sc);
        float torsoH = Px(0.55f * sc);
        float headH = Px(0.28f * sc);
        float legW = Px(0.30f * sc);
        float torsoW = Px(0.50f * sc);
        float headW = Px(0.24f * sc);

        DrawRect(new Rect2(fx - torsoW / 2f, fy, torsoW, 2f), Shadow);
        DrawRect(new Rect2(fx - legW / 2f, fy - legH, legW, legH), Pants);
        DrawRect(new Rect2(fx - torsoW / 2f, fy - legH - torsoH, torsoW, torsoH), Jersey);
        DrawRect(new Rect2(fx - headW / 2f, fy - legH - torsoH - headH, headW, headH), Skin);
        DrawRect(new Rect2(fx - headW / 2f - 1f, fy - legH - torsoH - headH - 2f, headW + 2f, Px(headH * 0.45f)), Helmet);

        Vector2 hands = Pt(bx + 0.40 * dir, 0.2, 0.95);
        float rad = Mathf.DegToRad(BatAngleDeg(s));
        Vector2 tip = hands + new Vector2(dir * Mathf.Cos(rad), Mathf.Sin(rad)) * (0.86f * PxPerMetre(0.2));
        DrawLine(hands, tip, BatColor, 3f);
    }

    private static float BatAngleDeg(PrototypeSession s)
    {
        double p = s.SwingProgress;
        if (p < 0.0)
        {
            double waggle = s.Settings.ReduceMotion ? 0.0 : 6.0 * Math.Sin(s.Now * 7.0);
            return (float)(CockedDeg + waggle);
        }
        if (p <= 1.0)
        {
            return (float)(CockedDeg * (1.0 - p));
        }
        return (float)(FollowThroughDeg * Math.Min((p - 1.0) / 0.8, 1.0));
    }

    private void DrawCursor(PrototypeSession s)
    {
        Vector2 center = Pt(s.CursorX, ArcadeTuning.ContactPlaneY, s.CursorZ);
        center = new Vector2(Px(center.X), Px(center.Y));
        float sc = PxPerMetre(ArcadeTuning.ContactPlaneY);
        float rx = (float)s.CursorHalfWidthM * sc;
        float ry = (float)s.CursorHalfHeightM * sc;
        Color color = s.CursorLocked ? CursorLockedColor : (s.PowerSwingActive ? CursorPowerColor : CursorColor);

        DrawEllipseOutline(center, rx, ry, color);
        DrawLine(center + new Vector2(-2f, 0f), center + new Vector2(2f, 0f), color, 1f);
        DrawLine(center + new Vector2(0f, -2f), center + new Vector2(0f, 2f), color, 1f);
    }

    private void DrawEllipseOutline(Vector2 center, float rx, float ry, Color color)
    {
        Vector2 previous = new(center.X + rx * UnitCos[0], center.Y + ry * UnitSin[0]);
        for (int i = 1; i <= EllipseSegments; i++)
        {
            Vector2 next = new(center.X + rx * UnitCos[i], center.Y + ry * UnitSin[i]);
            DrawLine(previous, next, color, 1f);
            previous = next;
        }
    }

    private void DrawPitchBall(PrototypeSession s)
    {
        if (!s.BallVisible)
        {
            return;
        }

        Vec3 b = s.BallPosition;
        Vector2 c = Pt(b.X, b.Y, b.Z);
        Vector2 ground = Pt(b.X, b.Y, 0.0);
        float r = Mathf.Max(1f, (float)ArcadeTuning.BallRadius * PxPerMetre(b.Y));

        DrawRect(new Rect2(Px(ground.X - r), Px(ground.Y), Px(2f * r) + 1f, 1f), Shadow);
        if (s.Phase == PrototypePhase.Flight && s.ReadCueOn)
        {
            DrawReadTrail(s);
        }
        float size = Mathf.Max(1f, Px(2f * r));
        DrawRect(new Rect2(Px(c.X - r), Px(c.Y - r), size, size), BallColor);
    }

    /// <summary>
    /// The discipline "read" (arcade §5.1): a trail along the ball's true path,
    /// tinted by pitch family. Color is paired with trail length (fastball 4
    /// dots, breaking 3, offspeed 2) so it never relies on color alone.
    /// </summary>
    private void DrawReadTrail(PrototypeSession s)
    {
        PitchKind kind = s.CurrentPitch;
        Color color = kind == PitchKind.Fastball ? CueFastball : (kind == PitchKind.Breaking ? CueBreaking : CueOffspeed);
        int dots = kind == PitchKind.Fastball ? 4 : (kind == PitchKind.Breaking ? 3 : 2);
        for (int i = 1; i <= dots; i++)
        {
            Vec3 p = s.PitchPositionAt(s.FlightTime - i * 0.03);
            Vector2 c = Pt(p.X, p.Y, p.Z);
            float r = Mathf.Max(1f, (float)ArcadeTuning.BallRadius * PxPerMetre(p.Y) * (1f - 0.18f * i));
            float size = Mathf.Max(1f, Px(2f * r));
            DrawRect(new Rect2(Px(c.X - r), Px(c.Y - r), size, size), color);
        }
    }

    private void DrawContactFlash(PrototypeSession s)
    {
        if (!s.ContactFlashActive)
        {
            return;
        }
        Vec3 p = s.ContactPoint;
        Vector2 c = Pt(p.X, p.Y, p.Z);
        c = new Vector2(Px(c.X), Px(c.Y));
        DrawCircle(c, 3f, new Color(1f, 1f, 1f, 0.7f));
        DrawLine(c + new Vector2(-5f, 0f), c + new Vector2(5f, 0f), CursorColor, 1f);
        DrawLine(c + new Vector2(0f, -5f), c + new Vector2(0f, 5f), CursorColor, 1f);
    }

    // ------------------------------------------------------------------
    // Hit view
    // ------------------------------------------------------------------

    private Vector2 HitPt(double x, double y, double z) =>
        new(
            (float)(160.0 + (x - _camX) * HitScale),
            (float)(HitFocusY - (y - _camY) * HitScale * HitDepthK - z * HitScale * HitHeightK));

    private void DrawHitView(PrototypeSession s)
    {
        DrawRect(new Rect2(-8, -8, 336, 196), Grass);

        // Mowing stripes every 10 m of depth: the cheapest way to make the camera glide readable.
        double baseY = Math.Floor((_camY - 30.0) / 10.0) * 10.0;
        for (int i = 0; i < 16; i++)
        {
            double wy = baseY + i * 10.0;
            long stripe = (long)Math.Floor(wy / 10.0);
            if ((stripe & 1L) != 0)
            {
                continue;
            }
            float yFar = HitPt(0.0, wy + 10.0, 0.0).Y;
            float yNear = HitPt(0.0, wy, 0.0).Y;
            DrawRect(new Rect2(-8, yFar, 336, yNear - yFar), GrassStripe);
        }

        // Infield: a dirt diamond with a grass diamond inside, the mound, and the bases.
        _quad[0] = HitPt(0.0, -4.0, 0.0);
        _quad[1] = HitPt(25.0, 19.4, 0.0);
        _quad[2] = HitPt(0.0, 45.0, 0.0);
        _quad[3] = HitPt(-25.0, 19.4, 0.0);
        DrawColoredPolygon(_quad, Dirt);
        _quad[0] = HitPt(0.0, 7.0, 0.0);
        _quad[1] = HitPt(13.5, 19.4, 0.0);
        _quad[2] = HitPt(0.0, 32.0, 0.0);
        _quad[3] = HitPt(-13.5, 19.4, 0.0);
        DrawColoredPolygon(_quad, GrassInfield);
        DrawCircle(HitPt(0.0, 18.44, 0.0), 5f, DirtMound);
        DrawBase(0.0, 0.0);
        DrawBase(19.4, 19.4);
        DrawBase(0.0, 38.8);
        DrawBase(-19.4, 19.4);

        DrawFence(s);

        if (s.BallVisible)
        {
            Vec3 b = s.BallPosition;
            Vector2 ground = HitPt(b.X, b.Y, 0.0);
            Vector2 c = HitPt(b.X, b.Y, b.Z);
            DrawRect(new Rect2(Px(ground.X) - 2f, Px(ground.Y), 4f, 1f), Shadow);
            DrawRect(new Rect2(Px(c.X) - 1f, Px(c.Y) - 1f, 3f, 3f), BallColor);
        }

        if (s.HasLanding && s.Phase == PrototypePhase.Result)
        {
            Vec3 l = s.LandingPosition;
            Vector2 at = HitPt(l.X, l.Y, 0.0);
            DrawLine(at + new Vector2(-3f, -3f), at + new Vector2(3f, 3f), Marker, 1f);
            DrawLine(at + new Vector2(3f, -3f), at + new Vector2(-3f, 3f), Marker, 1f);
        }
    }

    private void DrawBase(double x, double y)
    {
        Vector2 p = HitPt(x, y, 0.0);
        DrawRect(new Rect2(Px(p.X) - 2f, Px(p.Y) - 1f, 4f, 3f), Chalk);
    }

    /// <summary>The fence from the park's five standard distances, the foul lines out to it, and the wall's top edge at its real height.</summary>
    private void DrawFence(PrototypeSession s)
    {
        ParkDimensions park = s.Park;
        double wall = park.WallHeightM;
        Vector2 home = HitPt(0.0, 0.0, 0.0);
        Vector2 previousBase = default;
        Vector2 previousTop = default;
        bool havePrevious = false;

        for (int deg = -45; deg <= 45; deg += 3)
        {
            double d = park.FenceDistanceM(deg);
            double rad = deg * ArcadeTuning.DegToRad;
            double x = d * Math.Sin(rad);
            double y = d * Math.Cos(rad);
            Vector2 fenceBase = HitPt(x, y, 0.0);
            Vector2 fenceTop = HitPt(x, y, wall);

            if (havePrevious)
            {
                DrawLine(previousBase, fenceBase, FenceBase, 2f);
                DrawLine(previousTop, fenceTop, FenceTop, 1f);
            }
            else
            {
                DrawLine(home, fenceBase, Chalk, 1f);
            }
            if (deg == 45)
            {
                DrawLine(home, fenceBase, Chalk, 1f);
            }

            previousBase = fenceBase;
            previousTop = fenceTop;
            havePrevious = true;
        }
    }

    private static float[] BuildUnit(Func<double, double> f)
    {
        var table = new float[EllipseSegments + 1];
        for (int i = 0; i <= EllipseSegments; i++)
        {
            table[i] = (float)f(i * Math.PI * 2.0 / EllipseSegments);
        }
        return table;
    }
}

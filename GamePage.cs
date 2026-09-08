using Microsoft.Maui.Graphics;

namespace Tetris;

public class GamePage : ContentPage
{
    const int BoardW = 10;
    const int BoardH = 20;

    static float F(double v) => (float)v;

    // Фигуры: I O T S Z J L
    static readonly int[][] Shapes =
    {
        new[] { 0,0,0,0, 1,1,1,1, 0,0,0,0, 0,0,0,0 },
        new[] { 1,1,0,0, 1,1,0,0, 0,0,0,0, 0,0,0,0 },
        new[] { 0,1,0,0, 1,1,1,0, 0,0,0,0, 0,0,0,0 },
        new[] { 0,1,1,0, 1,1,0,0, 0,0,0,0, 0,0,0,0 },
        new[] { 1,1,0,0, 0,1,1,0, 0,0,0,0, 0,0,0,0 },
        new[] { 1,0,0,0, 1,1,1,0, 0,0,0,0, 0,0,0,0 },
        new[] { 0,0,1,0, 1,1,1,0, 0,0,0,0, 0,0,0,0 },
    };

    static readonly string[] ShapeHex =
    {
        "#00BFFF", // I голубой
        "#FFCC00", // O жёлтый
        "#9933FF", // T фиолетовый
        "#33CC33", // S зелёный
        "#FF3333", // Z красный
        "#3366FF", // J синий
        "#FF8000", // L оранжевый
    };

    readonly GraphicsView view;
    readonly IDispatcherTimer timer;

    int[,] board = new int[BoardH, BoardW];
    int[] piece = Array.Empty<int>();
    int pieceIdx;
    int px, py;
    int[] nextPiece = Array.Empty<int>();
    int nextIdx;

    int score, lines, level;
    int fallMs = 800;
    bool gameOver, paused;
    long acc;

    // метрики макета (пересчитываются при каждой отрисовке)
    double W, H, topBarH, cell, boardX, boardY, boardPxW, boardPxH, panelX;

    public GamePage()
    {
        Content = view = new GraphicsView
        {
            Drawable = new PageDrawable(this)
        };
        BackgroundColor = Color.FromArgb("#12121C");
        Title = "Тетрис";

        view.SizeChanged += (_, _) => { W = view.Width; H = view.Height; };
        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTap;
        view.GestureRecognizers.Add(tap);

        InitGame();

        timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(16);
        timer.Tick += OnTick;
        timer.Start();
    }

    // ---- Инициализация ----
    void InitGame()
    {
        board = new int[BoardH, BoardW];
        score = lines = level = 0;
        fallMs = 800;
        acc = 0;
        gameOver = false;
        paused = false;
        SpawnNext();
        SpawnPiece();
        view.Invalidate();
    }

    void SpawnNext()
    {
        nextIdx = Random.Shared.Next(Shapes.Length);
        nextPiece = (int[])Shapes[nextIdx].Clone();
    }

    void SpawnPiece()
    {
        piece = nextPiece;
        pieceIdx = nextIdx;
        px = 3;
        py = 0;
        SpawnNext();
        if (Collides(piece, px, py))
            gameOver = true;
    }

    // ---- Логика ----
    bool Collides(int[] p, int ox, int oy)
    {
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
            {
                if (p[y * 4 + x] == 0) continue;
                int nx = ox + x, ny = oy + y;
                if (nx < 0 || nx >= BoardW || ny >= BoardH) return true;
                if (ny >= 0 && board[ny, nx] != 0) return true;
            }
        return false;
    }

    static int[] Rotate(int[] p)
    {
        var r = new int[16];
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                r[x * 4 + (3 - y)] = p[y * 4 + x];
        return r;
    }

    void TryMove(int dx, int dy)
    {
        if (gameOver || paused) return;
        if (!Collides(piece, px + dx, py + dy))
        {
            px += dx;
            py += dy;
            view.Invalidate();
        }
    }

    void TryRotate()
    {
        if (gameOver || paused) return;
        var r = Rotate(piece);
        foreach (int kick in new[] { 0, -1, 1, -2, 2 })
        {
            if (!Collides(r, px + kick, py))
            {
                piece = r;
                px += kick;
                view.Invalidate();
                return;
            }
        }
    }

    void HardDrop()
    {
        if (gameOver || paused) return;
        while (!Collides(piece, px, py + 1))
        {
            py++;
            score += 2;
        }
        view.Invalidate();
        LockPiece();
    }

    void LockPiece()
    {
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                if (piece[y * 4 + x] != 0 && py + y >= 0)
                    board[py + y, px + x] = pieceIdx + 1;

        ClearLines();
        SpawnPiece();
        view.Invalidate();
    }

    void ClearLines()
    {
        int cleared = 0;
        for (int y = BoardH - 1; y >= 0; y--)
        {
            bool full = true;
            for (int x = 0; x < BoardW; x++)
                if (board[y, x] == 0) { full = false; break; }

            if (full)
            {
                cleared++;
                for (int yy = y; yy > 0; yy--)
                    for (int x = 0; x < BoardW; x++)
                        board[yy, x] = board[yy - 1, x];
                for (int x = 0; x < BoardW; x++) board[0, x] = 0;
                y++;
            }
        }
        if (cleared > 0)
        {
            int[] points = { 0, 100, 300, 500, 800 };
            score += points[Math.Min(cleared, 4)] * level;
            lines += cleared;
            level = lines / 10 + 1;
            fallMs = Math.Max(100, 800 - (level - 1) * 70);
        }
    }

    void OnTick(object? s, EventArgs e)
    {
        if (gameOver || paused) { acc = 0; return; }
        acc += 16;
        bool moved = false;
        while (acc >= fallMs)
        {
            acc -= fallMs;
            if (!Collides(piece, px, py + 1)) { py++; moved = true; }
            else { LockPiece(); break; }
        }
        if (moved) view.Invalidate();
    }

    // ---- Управление тапами ----
    void OnTap(object? sender, TappedEventArgs e)
    {
        if (gameOver) { InitGame(); return; }

        var pos = e.GetPosition(view);
        double tapX = pos?.X ?? 0;
        double tapY = pos?.Y ?? 0;
        if (tapY < topBarH)
        {
            if (tapX > W * 0.72) paused = !paused;
            else TryRotate();
            view.Invalidate();
            return;
        }
        if (paused)
        {
            paused = false;
            view.Invalidate();
            return;
        }
        double t = tapX / Math.Max(1, W);
        if (t < 1.0 / 3) TryMove(-1, 0);
        else if (t > 2.0 / 3) TryMove(1, 0);
        else HardDrop();
    }

    // ---- Макет ----
    void Layout()
    {
        double margin = Math.Max(8, W * 0.025);
        topBarH = Math.Max(46, H * 0.075);
        double panelW = Math.Min(W * 0.28, 150);
        double availW = W - panelW - margin * 3;
        double availH = H - topBarH - margin * 2;
        cell = Math.Floor(Math.Min(availW / BoardW, availH / BoardH));
        boardX = margin;
        boardY = topBarH + margin + (availH - cell * BoardH) / 2;
        boardPxW = cell * BoardW;
        boardPxH = cell * BoardH;
        panelX = boardX + boardPxW + margin * 1.5;
    }

    // ---- Отрисовка ----
    void Draw(ICanvas g)
    {
        if (W < 50 || H < 50) return;
        Layout();

        g.FillColor = Color.FromArgb("#12121C");
        g.FillRectangle(F(0), F(0), F(W), F(H));

        DrawTopBar(g);

        // фон поля
        g.FillColor = Colors.Black;
        g.FillRectangle(F(boardX), F(boardY), F(boardPxW), F(boardPxH));

        // сетка
        var grid = new PathF();
        for (int i = 0; i <= BoardW; i++)
            grid.MoveTo(F(boardX + i * cell), F(boardY)).LineTo(F(boardX + i * cell), F(boardY + boardPxH));
        for (int i = 0; i <= BoardH; i++)
            grid.MoveTo(F(boardX), F(boardY + i * cell)).LineTo(F(boardX + boardPxW), F(boardY + i * cell));
        g.StrokeColor = Color.FromArgb("#262640");
        g.StrokeSize = 1f;
        g.DrawPath(grid);

        // блоки
        var mainPaths = new PathF[Shapes.Length];
        for (int i = 0; i < mainPaths.Length; i++) mainPaths[i] = new PathF();
        var lightPath = new PathF();
        var darkPath = new PathF();
        var outline = new PathF();
        double pad = Math.Max(1.5, cell * 0.07);

        void AddBlock(int idx, double cx, double cy)
        {
            if (cy < 0) return;
            mainPaths[idx].AppendRectangle(F(cx + pad), F(cy + pad), F(cell - pad * 2), F(cell - pad * 2), true);
            double h = Math.Max(2, cell * 0.18);
            lightPath.AppendRectangle(F(cx + pad), F(cy + pad), F(cell - pad * 2), F(h), true);
            darkPath.AppendRectangle(F(cx + pad), F(cy + cell - pad - h), F(cell - pad * 2), F(h), true);
            outline.AppendRectangle(F(cx + pad), F(cy + pad), F(cell - pad * 2), F(cell - pad * 2), true);
        }

        for (int y = 0; y < BoardH; y++)
            for (int x = 0; x < BoardW; x++)
                if (board[y, x] > 0)
                    AddBlock(board[y, x] - 1, boardX + x * cell, boardY + y * cell);

        if (!gameOver)
        {
            int ty = py;
            while (!Collides(piece, px, ty + 1)) ty++;
            if (ty > py)
            {
                g.Alpha = 0.25f;
                g.FillColor = Colors.White;
                var ghost = new PathF();
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                        if (piece[y * 4 + x] != 0 && ty + y >= 0)
                            ghost.AppendRectangle(F(boardX + (px + x) * cell + pad * 2),
                                F(boardY + (ty + y) * cell + pad * 2),
                                F(cell - pad * 4), F(cell - pad * 4), true);
                g.FillPath(ghost, WindingMode.NonZero);
                g.Alpha = 1f;
            }
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    if (piece[y * 4 + x] != 0)
                        AddBlock(pieceIdx, boardX + (px + x) * cell, boardY + (py + y) * cell);
        }

        for (int i = 0; i < mainPaths.Length; i++)
        {
            g.FillColor = Color.FromArgb(ShapeHex[i]);
            g.FillPath(mainPaths[i], WindingMode.NonZero);
        }
        g.FillColor = new Color(1f, 1f, 1f, 0.35f);
        g.FillPath(lightPath, WindingMode.NonZero);
        g.FillColor = new Color(0f, 0f, 0f, 0.35f);
        g.FillPath(darkPath, WindingMode.NonZero);
        g.StrokeColor = new Color(1f, 1f, 1f, 0.85f);
        g.StrokeSize = 1f;
        g.DrawPath(outline);

        // рамка поля
        g.StrokeColor = Color.FromArgb("#00BFFF");
        g.StrokeSize = 2f;
        g.DrawRectangle(F(boardX - 2), F(boardY - 2), F(boardPxW + 4), F(boardPxH + 4));

        DrawPanel(g);
        DrawOverlay(g);
    }

    void SetFont(ICanvas g, double size, bool bold, Color color)
    {
        g.Font = new Microsoft.Maui.Graphics.Font(string.Empty, bold ? FontWeights.Bold : FontWeights.Normal, FontStyleType.Normal);
        g.FontSize = F(size);
        g.FontColor = color;
    }

    void DrawTopBar(ICanvas g)
    {
        double fs = topBarH * 0.38;
        SetFont(g, fs, true, Colors.White);
        g.DrawString($"СЧЁТ: {score}    УРОВЕНЬ: {level}", F(W * 0.30), F(topBarH * 0.68), HorizontalAlignment.Center);

        SetFont(g, fs * 0.75, true, Color.FromArgb("#FFCC00"));
        g.DrawString(paused ? "ПАУЗА" : "ПАУЗА | ПОВОРОТ", F(W * 0.84), F(topBarH * 0.65), HorizontalAlignment.Center);
    }

    void DrawPanel(ICanvas g)
    {
        if (panelX >= W - 20) return;
        double pw = W - panelX - 8;
        double cardH = Math.Min(86, cell * 3.1);
        double gap = 12;
        double y0 = boardY;

        double titleFs = cell * 0.42;
        double valueFs = cell * 0.8;

        void Card(double y, string title, string value, Color color)
        {
            g.FillColor = Color.FromArgb("#232340");
            g.FillRoundedRectangle(F(panelX), F(y), F(pw), F(cardH), F(10));
            g.StrokeColor = Color.FromArgb("#4B4B78");
            g.StrokeSize = 1f;
            g.DrawRoundedRectangle(F(panelX), F(y), F(pw), F(cardH), F(10));

            SetFont(g, titleFs, true, Color.FromArgb("#A0A0C3"));
            g.DrawString(title, F(panelX + 12), F(y + titleFs * 1.1), HorizontalAlignment.Left);

            SetFont(g, valueFs, true, color);
            g.DrawString(value, F(panelX + 12), F(y + cardH * 0.8), HorizontalAlignment.Left);
        }

        Card(y0, "СЧЁТ", score.ToString(), Color.FromArgb("#FFCC00"));
        Card(y0 + cardH + gap, "ЛИНИИ", lines.ToString(), Color.FromArgb("#33CC33"));
        Card(y0 + (cardH + gap) * 2, "УРОВЕНЬ", level.ToString(), Color.FromArgb("#00BFFF"));

        // окно «Дальше»
        double ny = y0 + (cardH + gap) * 3;
        double nh = Math.Min(96, cell * 3.4);
        g.FillColor = Color.FromArgb("#232340");
        g.FillRoundedRectangle(F(panelX), F(ny), F(pw), F(nh), F(10));
        g.StrokeColor = Color.FromArgb("#4B4B78");
        g.StrokeSize = 1f;
        g.DrawRoundedRectangle(F(panelX), F(ny), F(pw), F(nh), F(10));
        SetFont(g, titleFs, true, Color.FromArgb("#A0A0C3"));
        g.DrawString("ДАЛЬШЕ", F(panelX + 12), F(ny + titleFs * 1.1), HorizontalAlignment.Left);

        double sc = Math.Min(16, (Math.Min(pw, nh) - 40) / 4.0);
        double totalW = 4 * sc, totalH = 4 * sc;
        double ox = panelX + (pw - totalW) / 2;
        double oy = ny + 18 + (nh - 18 - totalH) / 2;
        g.FillColor = Color.FromArgb(ShapeHex[nextIdx]);
        var npMain = new PathF();
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                if (nextPiece[y * 4 + x] != 0)
                    npMain.AppendRectangle(F(ox + x * sc + 1), F(oy + y * sc + 1), F(sc - 2), F(sc - 2), true);
        g.FillPath(npMain, WindingMode.NonZero);
    }

    void DrawOverlay(ICanvas g)
    {
        if (!gameOver && !paused) return;
        g.Alpha = 0.78f;
        g.FillColor = Color.FromArgb("#0A0A14");
        g.FillRectangle(F(0), F(0), F(W), F(H));
        g.Alpha = 1f;

        string title = gameOver ? "ИГРА ОКОНЧЕНА" : "ПАУЗА";
        string? sub = gameOver ? $"Счёт: {score}" : null;
        string hint = gameOver ? "Тапните, чтобы начать заново" : "Тапните, чтобы продолжить";
        Color c = gameOver ? Color.FromArgb("#FF5050") : Color.FromArgb("#FFCC00");

        double fs = Math.Min(34, boardPxW * 0.075);
        double cx = boardX + boardPxW / 2;
        double cy = boardY + boardPxH / 3;

        SetFont(g, fs, true, c);
        g.DrawString(title, F(cx), F(cy), HorizontalAlignment.Center);
        if (sub != null)
            g.DrawString(sub, F(cx), F(cy + fs * 1.6), HorizontalAlignment.Center);
        SetFont(g, fs * 0.5, false, Colors.White);
        g.DrawString(hint, F(cx), F(cy + fs * (sub != null ? 2.8 : 2.0)), HorizontalAlignment.Center);
    }

    sealed class PageDrawable : IDrawable
    {
        readonly GamePage owner;
        public PageDrawable(GamePage owner) => this.owner = owner;
        public void Draw(ICanvas canvas, RectF dirtyRect) => owner.Draw(canvas);
    }
}

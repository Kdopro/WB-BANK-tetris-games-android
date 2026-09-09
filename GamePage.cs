using Microsoft.Maui.Graphics;

namespace Tetris;

public class GamePage : ContentPage
{
    const int BoardW = 10;
    const int BoardH = 20;
    const string TitleFont = "Ruslan Display";
    const string BodyFont = "Alegreya";

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

    (string Glyph, string Desc, Action Act)[] Buttons = Array.Empty<(string, string, Action)>();

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
    double W, H, titleH, cell, boardX, boardY, boardPxW, boardPxH;
    double panelX, panelY, panelW, panelCardH, nextCardH;
    double btnAreaTop;
    double[] btnX = new double[6];
    double[] btnY = new double[6];
    double btnS;

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

        Buttons = new (string, string, Action)[]
        {
            ("\u25C0", "\u0432\u043B\u0435\u0432\u043E", () => TryMove(-1, 0)),
            ("\u25B2", "\u043F\u043E\u0432\u043E\u0440\u043E\u0442", TryRotate),
            ("\u25BC", "\u0432\u043D\u0438\u0437", SoftDrop),
            ("\u25B6", "\u0432\u043F\u0440\u0430\u0432\u043E", () => TryMove(1, 0)),
            ("\u21B3", "\u0441\u0431\u0440\u043E\u0441", HardDrop),
            ("\u2758\u2758", "\u043F\u0430\u0443\u0437\u0430", TogglePause),
        };

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

    void SoftDrop()
    {
        if (gameOver || paused) return;
        if (!Collides(piece, px, py + 1))
        {
            py++;
            score++;
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

    void TogglePause()
    {
        if (gameOver) return;
        paused = !paused;
        view.Invalidate();
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

        for (int i = 0; i < Buttons.Length; i++)
            if (tapX >= btnX[i] && tapX <= btnX[i] + btnS && tapY >= btnY[i] && tapY <= btnY[i] + btnS)
            {
                Buttons[i].Act();
                return;
            }

        if (paused)
        {
            paused = false;
            view.Invalidate();
            return;
        }

        // тап-зоны по сторонам экрана
        if (tapY < boardY)
            TryRotate();                  // верх — поворот
        else if (tapY > boardY + boardPxH)
            HardDrop();                   // низ — быстрый сброс
        else if (tapX < W / 2)
            TryMove(-1, 0);               // левая сторона — влево
        else
            TryMove(1, 0);                // правая сторона — вправо
    }

    // ---- Макет (книжная раскладка, статистика справа) ----
    void Layout()
    {
        double margin = Math.Max(8, W * 0.035);
        titleH = Math.Max(44, H * 0.055);

        double controlsH = H * 0.21;
        btnS = Math.Min((W - margin * 2) / 4.6, controlsH * 0.78);

        // правая панель статистики
        panelW = Math.Min(W * 0.30, 150);
        double gap = margin;
        double boardAreaW = W - margin * 2 - panelW - gap;

        double availH = H - titleH - controlsH - margin * 3;
        cell = Math.Floor(Math.Min(boardAreaW / BoardW, availH / BoardH));
        boardX = margin;
        boardY = titleH + margin;
        boardPxW = cell * BoardW;
        boardPxH = cell * BoardH;

        // карточки правой панели: [Дальше] [Счёт] [Линии] [Уровень]
        panelX = margin + boardAreaW + gap;
        panelY = boardY;
        nextCardH = Math.Min(92, cell * 3.2);
        panelCardH = Math.Min(72, cell * 2.5);

        // кнопки: 2 ряда по 3
        double rowGap = 14;
        double descH = Math.Max(14, btnS * 0.26);
        double totalBtnH = btnS * 2 + rowGap + descH;
        double bottom = H - margin;
        double row1Y = bottom - totalBtnH;
        double row2Y = row1Y + btnS + rowGap;
        btnAreaTop = row1Y;
        double step = (W - margin * 2) / 3;
        for (int i = 0; i < 6; i++)
        {
            int row = i / 3, col = i % 3;
            double cellX = margin + col * step;
            btnX[i] = cellX + (step - btnS) / 2;
            btnY[i] = row == 0 ? row1Y : row2Y;
        }
    }

    // ---- Отрисовка ----
    void Draw(ICanvas g)
    {
        if (W < 50 || H < 50) return;
        Layout();

        g.FillColor = Color.FromArgb("#12121C");
        g.FillRectangle(F(0), F(0), F(W), F(H));

        DrawTitle(g);

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

        DrawRightPanel(g);
        DrawButtons(g);
        DrawOverlay(g);
    }

    void SetFont(ICanvas g, double size, bool bold, Color color, string family = BodyFont)
    {
        g.Font = new Microsoft.Maui.Graphics.Font(family, bold ? FontWeights.Bold : FontWeights.Normal, FontStyleType.Normal);
        g.FontSize = F(size);
        g.FontColor = color;
    }

    void DrawTitle(ICanvas g)
    {
        SetFont(g, titleH * 0.7, true, Color.FromArgb("#FFD700"), TitleFont);
        g.DrawString("\u0422\u0435\u0442\u0440\u0438\u0441", F(boardX + boardPxW / 2), F(titleH * 0.85), HorizontalAlignment.Center);
    }

    void DrawRightPanel(ICanvas g)
    {
        if (panelX >= W - 10) return;
        double gap = 8;
        double titleFs = panelCardH * 0.26;
        double valueFs = panelCardH * 0.46;
        double cy = panelY;

        void Card(double y, double h, string title, string value, Color color)
        {
            g.FillColor = Color.FromArgb("#232340");
            g.FillRoundedRectangle(F(panelX), F(y), F(panelW), F(h), F(10));
            g.StrokeColor = Color.FromArgb("#4B4B78");
            g.StrokeSize = 1f;
            g.DrawRoundedRectangle(F(panelX), F(y), F(panelW), F(h), F(10));

            SetFont(g, titleFs, true, Color.FromArgb("#A0A0C3"));
            g.DrawString(title, F(panelX + panelW / 2), F(y + titleFs * 0.9), HorizontalAlignment.Center);

            SetFont(g, valueFs, true, color);
            g.DrawString(value, F(panelX + panelW / 2), F(y + h * 0.84), HorizontalAlignment.Center);
        }

        // карточка «Дальше»
        g.FillColor = Color.FromArgb("#232340");
        g.FillRoundedRectangle(F(panelX), F(cy), F(panelW), F(nextCardH), F(10));
        g.StrokeColor = Color.FromArgb("#4B4B78");
        g.StrokeSize = 1f;
        g.DrawRoundedRectangle(F(panelX), F(cy), F(panelW), F(nextCardH), F(10));
        SetFont(g, titleFs, true, Color.FromArgb("#A0A0C3"));
        g.DrawString("Дальше", F(panelX + panelW / 2), F(cy + titleFs * 0.9), HorizontalAlignment.Center);

        double sc = Math.Min(15, (Math.Min(panelW, nextCardH) - 44) / 4.0);
        double totalW = 4 * sc, totalH = 4 * sc;
        double ox = panelX + (panelW - totalW) / 2;
        double oy = cy + titleFs * 1.2 + (nextCardH - titleFs * 1.2 - totalH) / 2;
        g.FillColor = Color.FromArgb(ShapeHex[nextIdx]);
        var npMain = new PathF();
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                if (nextPiece[y * 4 + x] != 0)
                    npMain.AppendRectangle(F(ox + x * sc + 1), F(oy + y * sc + 1), F(sc - 2), F(sc - 2), true);
        g.FillPath(npMain, WindingMode.NonZero);

        cy += nextCardH + gap;
        Card(cy, panelCardH, "Счёт", score.ToString(), Color.FromArgb("#FFCC00"));
        cy += panelCardH + gap;
        Card(cy, panelCardH, "Линии", lines.ToString(), Color.FromArgb("#33CC33"));
        cy += panelCardH + gap;
        Card(cy, panelCardH, "Уровень", level.ToString(), Color.FromArgb("#00BFFF"));
    }

    void DrawButtons(ICanvas g)
    {
        double descH = Math.Max(13, btnS * 0.24);
        double rad = btnS * 0.22;

        SetFont(g, Math.Max(11, btnS * 0.17), false, Color.FromArgb("#7A7AA0"));
        g.DrawString("\u2191 \u043F\u043E\u0432\u043E\u0440\u043E\u0442  \u00B7  \u2193 \u0441\u0431\u0440\u043E\u0441  \u00B7  \u2190/\u2192 \u0441\u0442\u043E\u0440\u043E\u043D\u044B \u044D\u043A\u0440\u0430\u043D\u0430", F(W / 2), F(btnAreaTop - descH * 0.3), HorizontalAlignment.Center);

        for (int i = 0; i < Buttons.Length; i++)
        {
            var (glyph, desc, _) = Buttons[i];
            double x = btnX[i], y = btnY[i];

            g.FillColor = paused ? new Color(0.14f, 0.14f, 0.22f, 1f) : Color.FromArgb("#232340");
            g.FillRoundedRectangle(F(x), F(y), F(btnS), F(btnS), F(rad));
            g.StrokeColor = paused ? Color.FromArgb("#3A3A5C") : Color.FromArgb("#00BFFF");
            g.StrokeSize = 1.5f;
            g.DrawRoundedRectangle(F(x), F(y), F(btnS), F(btnS), F(rad));

            SetFont(g, btnS * 0.4, true, Colors.White);
            g.DrawString(glyph, F(x + btnS / 2), F(y + btnS * 0.62), HorizontalAlignment.Center);

            SetFont(g, descH, true, Color.FromArgb("#A0A0C3"));
            g.DrawString(desc, F(x + btnS / 2), F(y + btnS + descH * 0.9), HorizontalAlignment.Center);
        }
    }

    void DrawOverlay(ICanvas g)
    {
        if (!gameOver && !paused) return;
        g.Alpha = 0.78f;
        g.FillColor = Color.FromArgb("#0A0A14");
        g.FillRectangle(F(0), F(0), F(W), F(H));
        g.Alpha = 1f;

        string title = gameOver ? "\u0418\u0433\u0440\u0430 \u043E\u043A\u043E\u043D\u0447\u0435\u043D\u0430" : "\u041F\u0430\u0443\u0437\u0430";
        string? sub = gameOver ? $"Счёт: {score}" : null;
        string hint = gameOver
            ? "Тапните — начать заново"
            : "Тап «Пауза» или поле — продолжить";
        Color c = gameOver ? Color.FromArgb("#FF5050") : Color.FromArgb("#FFCC00");

        double fs = Math.Min(40, boardPxW * 0.085);
        double cx = boardX + boardPxW / 2;
        double cy = boardY + boardPxH / 3;

        SetFont(g, fs, true, c, TitleFont);
        g.DrawString(title, F(cx), F(cy), HorizontalAlignment.Center);
        if (sub != null)
        {
            SetFont(g, fs * 0.6, true, c);
            g.DrawString(sub, F(cx), F(cy + fs * 1.5), HorizontalAlignment.Center);
        }
        SetFont(g, fs * 0.45, false, Colors.White);
        g.DrawString(hint, F(cx), F(cy + fs * (sub != null ? 2.5 : 1.9)), HorizontalAlignment.Center);
    }

    sealed class PageDrawable : IDrawable
    {
        readonly GamePage owner;
        public PageDrawable(GamePage owner) => this.owner = owner;
        public void Draw(ICanvas canvas, RectF dirtyRect) => owner.Draw(canvas);
    }
}

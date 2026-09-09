using Microsoft.Maui.Graphics;

namespace Tetris;

public class GamePage : ContentPage
{
    const int BoardW = 10;
    const int BoardH = 20;
    const string TitleFont = "Ruslan Display";
    const string BodyFont = "Alegreya";

    enum State { Intro, Playing, GameOver }

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

    // порядок кнопок: [0]=влево [1]=вниз [2]=вправо [3]=поворот [4]=сброс [5]=пауза
    (string Glyph, string Desc, Action Act)[] Buttons = Array.Empty<(string, string, Action)>();

    readonly GraphicsView view;
    readonly IDispatcherTimer timer;
    Android.Media.SoundPool? soundPool;
    int tapSoundId, dropSoundId;

    State state = State.Intro;
    double introStart;

    int[,] board = new int[BoardH, BoardW];
    int[] piece = Array.Empty<int>();
    int pieceIdx;
    int px, py;
    int[] nextPiece = Array.Empty<int>();
    int nextIdx;

    int score, lines, level;
    int fallMs = 800;
    bool paused;
    long acc;

    // подсветка нажатых кнопок
    int pressIdx = -1;
    long pressUntil;
    (double X, double Y) pressBtn;
    double pressUntilBtn;

    // метрики макета (пересчитываются при каждой отрисовке)
    double W, H, titleH, cell, boardX, boardY, boardPxW, boardPxH;
    double panelX, panelY, panelW, panelCardH, nextCardH;
    double btnAreaTop;
    double[] btnX = new double[6];
    double[] btnY = new double[6];
    double btnS;
    (double X, double Y, double W, double H) againBtnRect;
    (double X, double Y, double W, double H) exitBtnRect;

    public GamePage()
    {
        Content = view = new GraphicsView
        {
            Drawable = new PageDrawable(this)
        };
        BackgroundColor = Color.FromArgb("#12121C");
        Title = "Тетрис";

        InitSounds();

        view.SizeChanged += (_, _) => { W = view.Width; H = view.Height; };
        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTap;
        view.GestureRecognizers.Add(tap);

        Buttons = new (string, string, Action)[]
        {
            ("\u25C0", "\u0432\u043B\u0435\u0432\u043E", () => TryMove(-1, 0)),
            ("\u25BC", "\u0432\u043D\u0438\u0437", SoftDrop),
            ("\u25B6", "\u0432\u043F\u0440\u0430\u0432\u043E", () => TryMove(1, 0)),
            ("\u25B2", "\u043F\u043E\u0432\u043E\u0440\u043E\u0442", TryRotate),
            ("\u21B3", "\u0441\u0431\u0440\u043E\u0441", HardDrop),
            ("\u2758\u2758", "\u043F\u0430\u0443\u0437\u0430", TogglePause),
        };

        InitGame();

        introStart = Environment.TickCount64;
        timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(33);
        timer.Tick += OnTick;
        timer.Start();
    }

    void InitSounds()
    {
        try
        {
            var builder = new Android.Media.SoundPool.Builder().SetMaxStreams(4);
            if (builder == null) return;
            var pool = builder.Build();
            if (pool == null) return;
            soundPool = pool;
            tapSoundId = LoadSound(pool, "tap.wav");
            dropSoundId = LoadSound(pool, "drop.wav");
        }
        catch { soundPool = null; }
    }

    int LoadSound(Android.Media.SoundPool pool, string asset)
    {
        var file = Path.Combine(FileSystem.CacheDirectory, asset);
        if (!File.Exists(file))
        {
            using var src = File.OpenRead(asset);
            using var dst = File.Create(file);
            src.CopyTo(dst);
        }
        return pool.Load(file, 1);
    }

    void PlayTap()
    {
        try { soundPool?.Play(tapSoundId, 0.5f, 0.5f, 1, 0, 1f); } catch { }
    }

    void PlayDrop()
    {
        try { soundPool?.Play(dropSoundId, 0.7f, 0.7f, 1, 0, 0.7f); } catch { }
    }

    // ---- Инициализация ----
    void InitGame()
    {
        board = new int[BoardH, BoardW];
        score = lines = level = 0;
        fallMs = 800;
        acc = 0;
        state = State.Playing;
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
            state = State.GameOver;
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
        if (state != State.Playing || paused) return;
        if (!Collides(piece, px + dx, py + dy))
        {
            px += dx;
            py += dy;
            view.Invalidate();
        }
    }

    void SoftDrop()
    {
        if (state != State.Playing || paused) return;
        if (!Collides(piece, px, py + 1))
        {
            py++;
            score++;
            view.Invalidate();
        }
    }

    void TryRotate()
    {
        if (state != State.Playing || paused) return;
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
        if (state != State.Playing || paused) return;
        bool moved = false;
        while (!Collides(piece, px, py + 1))
        {
            py++;
            score += 2;
            moved = true;
        }
        view.Invalidate();
        if (moved) PlayDrop();
        LockPiece();
    }

    void TogglePause()
    {
        if (state != State.Playing) return;
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
        long now = Environment.TickCount64;
        bool dirty = false;

        if (state == State.Intro)
        {
            dirty = true; // анимация частиц
            if (now - introStart > 2600)
                InitGame();
            if (dirty) view.Invalidate();
            return;
        }

        if (state != State.Playing) return;
        if (now >= pressUntil) { pressIdx = -1; dirty = true; }
        if (now >= pressUntilBtn) { pressBtn = (0, 0); dirty = true; }

        if (paused) { acc = 0; if (dirty) view.Invalidate(); return; }
        acc += 33;
        bool moved = false;
        while (acc >= fallMs)
        {
            acc -= fallMs;
            if (!Collides(piece, px, py + 1)) { py++; moved = true; }
            else { LockPiece(); break; }
        }
        if (moved || dirty) view.Invalidate();
    }

    void SetPress(int idx)
    {
        pressIdx = idx;
        pressUntil = Environment.TickCount64 + 130;
    }

    // ---- Управление тапами ----
    void OnTap(object? sender, TappedEventArgs e)
    {
        var pos = e.GetPosition(view);
        double tapX = pos?.X ?? 0;
        double tapY = pos?.Y ?? 0;

        if (state == State.Intro)
        {
            introStart = Environment.TickCount64; // тап во время заставки просто ждёт
            return;
        }

        if (state == State.GameOver)
        {
            var again = againBtnRect;
            var exit = exitBtnRect;
            if (tapX >= again.X && tapX <= again.X + again.W && tapY >= again.Y && tapY <= again.Y + again.H)
            {
                PlayTap();
                pressBtn = (again.X + again.W / 2, again.Y + again.H / 2);
                pressUntilBtn = Environment.TickCount64 + 180;
                InitGame();
            }
            else if (tapX >= exit.X && tapX <= exit.X + exit.W && tapY >= exit.Y && tapY <= exit.Y + exit.H)
            {
                PlayTap();
                pressBtn = (exit.X + exit.W / 2, exit.Y + exit.H / 2);
                pressUntilBtn = Environment.TickCount64 + 180;
                MainActivity.Current?.FinishAffinity();
            }
            return;
        }

        // кнопки
        for (int i = 0; i < Buttons.Length; i++)
            if (tapX >= btnX[i] && tapX <= btnX[i] + btnS && tapY >= btnY[i] && tapY <= btnY[i] + btnS)
            {
                PlayTap();
                SetPress(i);
                Buttons[i].Act();
                return;
            }

        if (paused)
        {
            paused = false;
            PlayTap();
            view.Invalidate();
            return;
        }

        // тап-зоны по сторонам экрана
        if (tapY < boardY)
        {
            TryRotate();           // верх — поворот
            SetPress(3);
        }
        else if (tapY > boardY + boardPxH)
            HardDrop();            // низ — быстрый сброс
        else if (tapX < W / 2)
        {
            TryMove(-1, 0);        // левая сторона — влево
            SetPress(0);
        }
        else
        {
            TryMove(1, 0);         // правая сторона — вправо
            SetPress(2);
        }
    }

    // ---- Макет (книжная раскладка, статистика справа) ----
    void Layout()
    {
        double margin = Math.Max(8, W * 0.035);
        titleH = Math.Max(44, H * 0.055);

        // кнопки: клавиатурный расклад, на 20% меньше, приподняты выше
        double controlsH = H * 0.19;
        btnS = Math.Min((W - margin * 2) / 4.8, controlsH * 0.72) * 0.8;

        double availH = H - titleH - controlsH - margin * 3;
        cell = Math.Floor(Math.Min((W - margin * 2) / BoardW, availH / BoardH));
        boardX = margin;
        boardY = titleH + margin;
        boardPxW = cell * BoardW;
        boardPxH = cell * BoardH;

        // правая панель статистики
        panelW = Math.Min(W * 0.30, 150);
        double gap = margin;
        double boardAreaW = W - margin * 2 - panelW - gap;
        panelX = margin + boardAreaW + gap;
        panelY = boardY;
        nextCardH = Math.Min(92, cell * 3.2);
        panelCardH = Math.Min(72, cell * 2.5);

        // кнопки: 4 колонки, 2 ряда. Стрелки — крест как на клавиатуре, пауза правее.
        double rowGap = btnS * 0.30;
        double descH = Math.Max(12, btnS * 0.26);
        double totalBtnH = btnS * 2 + rowGap + descH;
        double bottom = H - margin - H * 0.015;   // приподнять над нижней кромкой
        double row1Y = bottom - totalBtnH;
        double row2Y = row1Y + btnS + rowGap;
        btnAreaTop = row1Y;

        double step = (W - margin * 2) / 4;
        double col(double i) => margin + i * step + (step - btnS) / 2;

        // ряд 1: ▲ поворот (над «вниз») + ⏸ пауза справа
        btnX[3] = col(1); btnY[3] = row1Y;   // ▲ поворот
        btnX[5] = col(3); btnY[5] = row1Y;   // ⏸ пауза — правее
        // ряд 2: ◀ влево, ▼ вниз, ▶ вправо, ⬇ сброс
        btnX[0] = col(0); btnY[0] = row2Y;   // ◀ влево
        btnX[1] = col(1); btnY[1] = row2Y;   // ▼ вниз
        btnX[2] = col(2); btnY[2] = row2Y;   // ▶ вправо
        btnX[4] = col(3); btnY[4] = row2Y;   // ⬇ сброс
    }

    // ---- Отрисовка ----
    void Draw(ICanvas g)
    {
        if (W < 50 || H < 50) return;
        Layout();

        g.FillColor = Color.FromArgb("#12121C");
        g.FillRectangle(F(0), F(0), F(W), F(H));

        if (state == State.Intro)
        {
            DrawIntro(g);
            return;
        }

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

        if (state == State.Playing)
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

        if (state == State.GameOver)
            DrawGameOver(g);
        else if (paused)
            DrawPauseOverlay(g);
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

    // ---- Заставка «KDOPROG presents games Tetris» ----
    void DrawIntro(ICanvas g)
    {
        double t = (Environment.TickCount64 - introStart) / 1000.0;
        double pulse = 0.5 + 0.5 * Math.Sin(t * 2.4);

        // падающие блошки по краям
        for (int i = 0; i < 14; i++)
        {
            double speed = 0.10 + (i % 5) * 0.03;
            double bx = W * (0.04 + 0.09 * (i % 7));
            if (i > 6) bx = W - W * (0.04 + 0.09 * ((i - 7) % 7));
            double by = (t * speed * H + i * H * 0.13) % (H * 1.2) - H * 0.1;
            double bs = Math.Max(12, W * 0.045);
            var c = Color.FromArgb(ShapeHex[i % ShapeHex.Length]);
            g.Alpha = 0.55f;
            g.FillColor = c;
            g.FillRoundedRectangle(F(bx), F(by), F(bs), F(bs), F(bs * 0.18));
            g.Alpha = 1f;
        }

        double cx = W / 2;
        double fs1 = Math.Min(W * 0.105, 44);
        SetFont(g, fs1 * 0.62, true, Color.FromArgb("#00BFFF"), BodyFont);
        g.DrawString("KDOPROG", F(cx), F(H * 0.28), HorizontalAlignment.Center);

        SetFont(g, fs1 * 0.4, false, Color.FromArgb("#A0A0C3"), BodyFont);
        g.DrawString("presents games", F(cx), F(H * 0.28 + fs1 * 0.85), HorizontalAlignment.Center);

        double fs2 = Math.Min(W * 0.24, 92) * (1 + 0.04 * pulse);
        SetFont(g, fs2, true, Color.FromArgb("#FFD700"), TitleFont);
        g.DrawString("\u0422\u0415\u0422\u0420\u0418\u0421", F(cx), F(H * 0.52), HorizontalAlignment.Center);

        // декоративный ряд фигурок
        double iy = H * 0.62;
        for (int i = 0; i < 7; i++)
        {
            var c = Color.FromArgb(ShapeHex[i]);
            g.FillColor = c;
            g.FillRoundedRectangle(F(cx - 3 * W * 0.075 + i * W * 0.075 - W * 0.017), F(iy), F(W * 0.034), F(W * 0.034), F(W * 0.006));
        }

        SetFont(g, fs1 * 0.34, false, new Color(F(0.75 + 0.25 * pulse), F(0.8 + 0.2 * pulse), 1f, 1f), BodyFont);
        g.DrawString("\u0442\u0430\u043F\u043D\u0438\u0442\u0435 \u0434\u043B\u044F \u043D\u0430\u0447\u0430\u043B\u0430", F(cx), F(H * 0.86), HorizontalAlignment.Center);
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
        double descH = Math.Max(12, btnS * 0.24);
        double rad = btnS * 0.22;
        long now = Environment.TickCount64;

        SetFont(g, Math.Max(10, btnS * 0.16), false, Color.FromArgb("#7A7AA0"));
        g.DrawString("\u2191 \u043F\u043E\u0432\u043E\u0440\u043E\u0442 \u00B7 \u2193 \u0441\u0431\u0440\u043E\u0441 \u00B7 \u2190/\u2192 \u0441\u0442\u043E\u0440\u043E\u043D\u044B \u044D\u043A\u0440\u0430\u043D\u0430", F(W / 2), F(btnAreaTop - descH * 0.4), HorizontalAlignment.Center);

        for (int i = 0; i < Buttons.Length; i++)
        {
            var (glyph, desc, _) = Buttons[i];
            double x = btnX[i], y = btnY[i];
            bool pressed = i == pressIdx && now < pressUntil;

            // свечение при нажатии
            if (pressed)
            {
                g.Alpha = 0.35f;
                g.FillColor = Color.FromArgb("#00E5FF");
                g.FillRoundedRectangle(F(x - 4), F(y - 4), F(btnS + 8), F(btnS + 8), F(rad + 4));
                g.Alpha = 1f;
            }

            g.FillColor = pressed
                ? Color.FromArgb("#35C4FF")
                : (paused && i != 5 ? Color.FromArgb("#1A1A2E") : Color.FromArgb("#232340"));
            g.FillRoundedRectangle(F(x), F(y), F(btnS), F(btnS), F(rad));
            g.StrokeColor = pressed ? Color.FromArgb("#FFFFFF") : Color.FromArgb("#00BFFF");
            g.StrokeSize = pressed ? 2.5f : 1.5f;
            g.DrawRoundedRectangle(F(x), F(y), F(btnS), F(btnS), F(rad));

            SetFont(g, btnS * 0.42, true, pressed ? Color.FromArgb("#002233") : Colors.White);
            g.DrawString(glyph, F(x + btnS / 2), F(y + btnS * 0.62), HorizontalAlignment.Center);

            SetFont(g, descH, true, pressed ? Color.FromArgb("#BFE9FF") : Color.FromArgb("#A0A0C3"));
            g.DrawString(desc, F(x + btnS / 2), F(y + btnS + descH * 0.9), HorizontalAlignment.Center);
        }

        // подсветка кнопки, нажатой из заставки/тап-зоны (центр-точка)
        if (pressBtn.X > 0 && now < pressUntilBtn)
        {
            g.Alpha = 0.35f;
            g.FillColor = Color.FromArgb("#FFD700");
            double pr = Math.Max(30, btnS * 0.5);
            g.FillEllipse(F(pressBtn.X - pr), F(pressBtn.Y - pr), F(pr * 2), F(pr * 2));
            g.Alpha = 1f;
        }
    }

    void DrawBigButton(ICanvas g, double x, double y, double w, double h, string text, Color bg, Color stroke)
    {
        double r = Math.Min(14, h * 0.35);
        g.FillColor = bg;
        g.FillRoundedRectangle(F(x), F(y), F(w), F(h), F(r));
        g.StrokeColor = stroke;
        g.StrokeSize = 2f;
        g.DrawRoundedRectangle(F(x), F(y), F(w), F(h), F(r));
        SetFont(g, h * 0.36, true, Colors.White, TitleFont);
        g.DrawString(text, F(x + w / 2), F(y + h * 0.62), HorizontalAlignment.Center);
    }

    // ---- Экран конца игры ----
    void DrawGameOver(ICanvas g)
    {
        g.Alpha = 0.85f;
        g.FillColor = Color.FromArgb("#05050C");
        g.FillRectangle(F(0), F(0), F(W), F(H));
        g.Alpha = 1f;

        double t = (Environment.TickCount64 - introStart) / 1000.0;
        double pulse = 0.5 + 0.5 * Math.Sin(t * 2.0);
        double cx = W / 2;

        double bw = Math.Min(W * 0.86, 360);
        double bh = Math.Min(H * 0.52, 460);
        double bx = (W - bw) / 2;
        double by = (H - bh) / 2 - H * 0.02;

        // рамка-карточка с золотым свечением
        g.Alpha = F(0.25 + 0.15 * pulse);
        g.FillColor = Color.FromArgb("#FFD700");
        g.FillRoundedRectangle(F(bx - 6), F(by - 6), F(bw + 12), F(bh + 12), F(22));
        g.Alpha = 1f;
        g.FillColor = Color.FromArgb("#191928");
        g.FillRoundedRectangle(F(bx), F(by), F(bw), F(bh), F(18));
        g.StrokeColor = Color.FromArgb("#FFD700");
        g.StrokeSize = 2f;
        g.DrawRoundedRectangle(F(bx), F(by), F(bw), F(bh), F(18));

        SetFont(g, Math.Min(bw * 0.13, 34), true, Color.FromArgb("#FFD700"), TitleFont);
        g.DrawString("\u0418\u0433\u0440\u0430 \u043E\u043A\u043E\u043D\u0447\u0435\u043D\u0430", F(cx), F(by + bh * 0.13), HorizontalAlignment.Center);

        // декоративная линия
        g.StrokeColor = Color.FromArgb("#4B4B78");
        g.StrokeSize = 1.5f;
        var line = new PathF();
        line.MoveTo(F(bx + bw * 0.15), F(by + bh * 0.20)).LineTo(F(bx + bw * 0.85), F(by + bh * 0.20));
        g.DrawPath(line);

        double rowY = by + bh * 0.30;
        double rowH = bh * 0.115;
        double fs = Math.Min(rowH * 0.5, 20);
        (string, string, Color)[] stats =
        {
            ("Счёт", score.ToString(), Color.FromArgb("#FFCC00")),
            ("Линии", lines.ToString(), Color.FromArgb("#33CC33")),
            ("Уровень", level.ToString(), Color.FromArgb("#00BFFF")),
        };
        foreach (var (label, value, color) in stats)
        {
            SetFont(g, fs, true, Color.FromArgb("#A0A0C3"));
            g.DrawString(label, F(bx + bw * 0.22), F(rowY + rowH * 0.7), HorizontalAlignment.Center);
            SetFont(g, fs * 1.25, true, color, TitleFont);
            g.DrawString(value, F(bx + bw * 0.75), F(rowY + rowH * 0.75), HorizontalAlignment.Center);
            rowY += rowH;
        }

        double btnW = (bw - 3 * bw * 0.06) / 2;
        double btnH = Math.Min(bh * 0.14, 54);
        double btnY = by + bh * 0.78;
        double b1x = bx + bw * 0.06;
        double b2x = bx + bw * 0.06 + btnW + bw * 0.06;
        againBtnRect = (b1x, btnY, btnW, btnH);
        exitBtnRect = (b2x, btnY, btnW, btnH);
        DrawBigButton(g, b1x, btnY, btnW, btnH, "\u0417\u0430\u043D\u043E\u0432\u043E", Color.FromArgb("#0E8A46"), Color.FromArgb("#33CC33"));
        DrawBigButton(g, b2x, btnY, btnW, btnH, "\u0412\u044B\u0445\u043E\u0434", Color.FromArgb("#8A2020"), Color.FromArgb("#FF5050"));
    }

    void DrawPauseOverlay(ICanvas g)
    {
        g.Alpha = 0.78f;
        g.FillColor = Color.FromArgb("#0A0A14");
        g.FillRectangle(F(0), F(0), F(W), F(H));
        g.Alpha = 1f;

        double fs = Math.Min(40, boardPxW * 0.085);
        double cx = boardX + boardPxW / 2;
        double cy = boardY + boardPxH / 3;

        SetFont(g, fs, true, Color.FromArgb("#FFCC00"), TitleFont);
        g.DrawString("\u041F\u0430\u0443\u0437\u0430", F(cx), F(cy), HorizontalAlignment.Center);
        SetFont(g, fs * 0.45, false, Colors.White);
        g.DrawString("\u0422\u0430\u043F \u00AB\u041F\u0430\u0443\u0437\u0430\u00BB \u0438\u043B\u0438 \u043F\u043E \u043F\u043E\u043B\u044E \u2014 \u043F\u0440\u043E\u0434\u043E\u043B\u0436\u0438\u0442\u044C", F(cx), F(cy + fs * 1.9), HorizontalAlignment.Center);
    }

    sealed class PageDrawable : IDrawable
    {
        readonly GamePage owner;
        public PageDrawable(GamePage owner) => this.owner = owner;
        public void Draw(ICanvas canvas, RectF dirtyRect) => owner.Draw(canvas);
    }
}

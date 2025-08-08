using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace ConsoleApp1;

class RgbTriangleWindow : GameWindow
{
    // --- Треугольник ---
    private int _triVao, _triVbo, _triShader;
    private double _time;
    private bool _isRunning = true;
    private float _rotationDir = 1f;   // 1 — по часовой, -1 — против
    private float _rotationSpeed = 1f; // рад/с

    // --- Полноэкранный квад (GUI/дым/виньетка) ---
    private int _quadVao, _quadVbo, _quadEbo;
    private int _guiShader;       // uMode: 0 — прямоугольник (кнопка), 1 — круглая «дымка»
    private int _vignetteShader;  // виньетка поверх

    // Ортографическая проекция (экран в пикселях, (0,0) — верхний левый)
    private Matrix4 _proj;

    // --- Кнопки ---
    private struct RectF { public float X, Y, W, H; public RectF(float x, float y, float w, float h){X=x;Y=y;W=w;H=h;} }
    private struct Button { public RectF Rect; public Vector4 Color; public int Id; }
    private readonly List<Button> _buttons = new();
    private bool _prevMouseDown = false;
    private bool _smokeEnabled = true;

    // --- Дым (простые частицы) ---
    private struct Particle { public Vector2 Pos, Vel; public float Radius, Life; }
    private readonly List<Particle> _particles = new();
    private readonly Random _rng = new();

    // --- Вершины треугольника: позиция + цвет ---
    private readonly float[] _triVertices =
    {
        0.0f,  0.6f, 0.0f,   1f, 0f, 0f,
        -0.6f, -0.6f, 0.0f,   0f, 1f, 0f,
        0.6f, -0.6f, 0.0f,   0f, 0f, 1f,
    };

    // --- Треугольник: шейдеры (оканчиваются переводом строки) ---
    private const string TriVert = @"#version 330 core
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aColor;

out vec3 vColor;

uniform float uTime;
uniform mat4 uModel;

void main()
{
    gl_Position = uModel * vec4(aPos, 1.0);
    vec3 wave = 0.5 + 0.5 * sin(uTime + vec3(0.0, 2.094, 4.188));
    vColor = aColor * wave;
}
";
    
    //Фрагментный шейдер для отрисовки треугольника.
    private const string TriFrag = @"#version 330 core
in vec3 vColor;
out vec4 FragColor;

void main()
{
    FragColor = vec4(vColor, 1.0);
}
";

    // --- Универсальный шейдер для GUI и дыма ---
    private const string GuiVert = @"#version 330 core
layout (location = 0) in vec2 aPos;
layout (location = 1) in vec2 aUV;

uniform mat4 uProj;
uniform mat4 uModel;

out vec2 vUV;

void main()
{
    vUV = aUV;
    gl_Position = uProj * uModel * vec4(aPos, 0.0, 1.0);
}
"; 
    
    //Фрагментный шейдер для GUI и эффекта дыма.
    private const string GuiFrag = @"#version 330 core
in vec2 vUV;
out vec4 FragColor;

uniform vec4 uColor;
uniform int uMode; // 0: solid rect, 1: radial smoke

void main()
{
    if (uMode == 0)
    {
        FragColor = uColor;
    }
    else
    {
        vec2 p = vUV * 2.0 - 1.0;  // -1..1
        float d = length(p);
        float alpha = smoothstep(1.0, 0.6, d);
        FragColor = vec4(uColor.rgb, uColor.a * alpha);
    }
}
";

    // --- Виньетка ---
    private const string VignetteVert = @"#version 330 core
layout (location = 0) in vec2 aPos;
layout (location = 1) in vec2 aUV;

uniform mat4 uProj;
uniform mat4 uModel;

out vec2 vUV;

void main()
{
    vUV = aUV;
    gl_Position = uProj * uModel * vec4(aPos, 0.0, 1.0);
}
";
    
    //Фрагментный шейдер для эффекта виньетки.
    private const string VignetteFrag = @"#version 330 core
in vec2 vUV;
out vec4 FragColor;

uniform float uStrength; // 0..1

void main()
{
    float d = length(vUV - vec2(0.5));
    float alpha = smoothstep(0.4, 0.9, d) * uStrength;
    FragColor = vec4(0.0, 0.0, 0.0, alpha);
}
";
    
    //Конструктор
    public RgbTriangleWindow()
        : base(GameWindowSettings.Default, new NativeWindowSettings
        {
            Title = "RGB Triangle — Effects + GUI (OpenTK)",
            Size = new Vector2i(900, 600),
        })
    { }
    
    //Инициализирует OpenGL-состояния, загружает шейдеры, VAO/VBO, GUI и эффекты.
    protected override void OnLoad()
    {
        base.OnLoad();

        Console.WriteLine("GL Vendor:   " + GL.GetString(StringName.Vendor));
        Console.WriteLine("GL Renderer: " + GL.GetString(StringName.Renderer));
        Console.WriteLine("GL Version:  " + GL.GetString(StringName.Version));
        Console.WriteLine("GLSL:        " + GL.GetString(StringName.ShadingLanguageVersion));

        GL.ClearColor(0.08f, 0.08f, 0.10f, 1f);
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        // Треугольник
        _triShader = CreateShaderProgram(TriVert, TriFrag);
        _triVao = GL.GenVertexArray();
        _triVbo = GL.GenBuffer();

        GL.BindVertexArray(_triVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _triVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, _triVertices.Length * sizeof(float), _triVertices, BufferUsageHint.StaticDraw);

        int stride = 6 * sizeof(float);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);
        GL.BindVertexArray(0);

        // Юнит-квад для GUI/дыма/виньетки
        float[] quad =
        {
            // x, y,  u, v
            0f, 0f,  0f, 0f,
            1f, 0f,  1f, 0f,
            1f, 1f,  1f, 1f,
            0f, 1f,  0f, 1f
        };
        uint[] indices = { 0, 1, 2, 2, 3, 0 };

        _quadVao = GL.GenVertexArray();
        _quadVbo = GL.GenBuffer();
        _quadEbo = GL.GenBuffer();

        GL.BindVertexArray(_quadVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _quadVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, quad.Length * sizeof(float), quad, BufferUsageHint.StaticDraw);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _quadEbo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);

        int qStride = 4 * sizeof(float);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, qStride, 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, qStride, 2 * sizeof(float));
        GL.EnableVertexAttribArray(1);
        GL.BindVertexArray(0);

        // GUI и виньетка
        _guiShader = CreateShaderProgram(GuiVert, GuiFrag);
        _vignetteShader = CreateShaderProgram(VignetteVert, VignetteFrag);

        // Ортопроекция
        _proj = Matrix4.CreateOrthographicOffCenter(0, Size.X, Size.Y, 0, -1, 1);

        // Кнопки и дым
        RebuildButtons();
        InitParticles(64);

        VSync = VSyncMode.On;
    }
    
    //Обновляет вьюпорт OpenGL, ортопроекцию и пересоздаёт интерфейс.
    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);
        GL.Viewport(0, 0, e.Width, e.Height);
        _proj = Matrix4.CreateOrthographicOffCenter(0, e.Width, e.Height, 0, -1, 1);
        RebuildButtons();
    }
    
    //Вызывается с частотой обновления окна.
    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        base.OnUpdateFrame(args);

        if (_isRunning) _time += args.Time;
        if (_smokeEnabled) UpdateParticles((float)args.Time);

        // Мышь: нажатие
        bool mouseDown = MouseState.IsButtonDown(MouseButton.Left);
        if (mouseDown && !_prevMouseDown)
        {
            var mp = MousePosition;
            float mx = (float)mp.X, my = (float)mp.Y;

            foreach (var b in _buttons)
            {
                if (PointInRect(mx, my, b.Rect))
                {
                    HandleButton(b.Id);
                    break;
                }
            }
        }
        _prevMouseDown = mouseDown;

        if (KeyboardState.IsKeyDown(Keys.Escape)) Close();
    }
    
    //Вызывается автоматически с частотой обновления окна.
    protected override void OnRenderFrame(FrameEventArgs args)
    {
        
        base.OnRenderFrame(args);

        GL.Clear(ClearBufferMask.ColorBufferBit);

        // 1) Дым (фон)
        if (_smokeEnabled) DrawSmoke();

        // 2) Треугольник
        GL.UseProgram(_triShader);
        int timeLoc = GL.GetUniformLocation(_triShader, "uTime");
        GL.Uniform1(timeLoc, (float)_time);

        float angle = (float)(_time * _rotationSpeed * _rotationDir);
        
        float cx = 0f;
        float cy = -0.2f;

        Matrix4 modelTri =
            Matrix4.CreateTranslation(-cx, -cy, 0f) *
            Matrix4.CreateRotationZ(angle) *
            Matrix4.CreateTranslation(cx, cy + 0.2f, 0f);
        
        int modelLoc = GL.GetUniformLocation(_triShader, "uModel");
        GL.UniformMatrix4(modelLoc, false, ref modelTri);

        GL.BindVertexArray(_triVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        // 3) Виньетка поверх
        DrawVignette(0.6f);

        // 4) Кнопки
        DrawButtons();

        SwapBuffers();
    }
    
    //Вызывается при закрытии окна для освобождения всех ресурсов OpenGL.
    protected override void OnUnload()
    {
        base.OnUnload();

        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.BindVertexArray(0);
        GL.UseProgram(0);

        void SafeDel<T>(Action<T> del, T obj){ try { del(obj); } catch {} }

        if (_triVbo != 0) SafeDel<int>(GL.DeleteBuffer, _triVbo);
        if (_triVao != 0) SafeDel<int>(GL.DeleteVertexArray, _triVao);
        if (_triShader != 0) SafeDel<int>(GL.DeleteProgram, _triShader);

        if (_quadEbo != 0) SafeDel<int>(GL.DeleteBuffer, _quadEbo);
        if (_quadVbo != 0) SafeDel<int>(GL.DeleteBuffer, _quadVbo);
        if (_quadVao != 0) SafeDel<int>(GL.DeleteVertexArray, _quadVao);
        if (_guiShader != 0) SafeDel<int>(GL.DeleteProgram, _guiShader);
        if (_vignetteShader != 0) SafeDel<int>(GL.DeleteProgram, _vignetteShader);
    }

    // ===================== Рисование и логика GUI/дыма =====================
    
    //Рисует виньетку — затемнение по краям экрана с заданной интенсивностью.
    private void DrawVignette(float strength)
    {
        GL.UseProgram(_vignetteShader);
        GL.BindVertexArray(_quadVao);

        int projLoc = GL.GetUniformLocation(_vignetteShader, "uProj");
        GL.UniformMatrix4(projLoc, false, ref _proj);

        Matrix4 model = MakeRectModel(0, 0, Size.X, Size.Y);
        int modelLoc = GL.GetUniformLocation(_vignetteShader, "uModel");
        GL.UniformMatrix4(modelLoc, false, ref model);

        int sLoc = GL.GetUniformLocation(_vignetteShader, "uStrength");
        GL.Uniform1(sLoc, strength);

        GL.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, 0);
    }
    
    //Рисует кнопки GUI с подсветкой при наведении мыши.
    private void DrawButtons()
    {
        GL.UseProgram(_guiShader);
        GL.BindVertexArray(_quadVao);

        int projLoc = GL.GetUniformLocation(_guiShader, "uProj");
        GL.UniformMatrix4(projLoc, false, ref _proj);
        int modeLoc = GL.GetUniformLocation(_guiShader, "uMode");
        int colorLoc = GL.GetUniformLocation(_guiShader, "uColor");
        int modelLoc = GL.GetUniformLocation(_guiShader, "uModel");

        var mp = MousePosition;
        float mx = (float)mp.X, my = (float)mp.Y;

        foreach (var b in _buttons)
        {
            bool hover = PointInRect(mx, my, b.Rect);

            Vector4 color = b.Color;
            if (b.Id == 0) color = _isRunning ? new Vector4(0.2f, 0.75f, 0.3f, 0.95f)
                : new Vector4(0.85f, 0.25f, 0.25f, 0.95f);
            if (b.Id == 3) color = _smokeEnabled ? new Vector4(0.2f, 0.7f, 0.8f, 0.95f)
                : new Vector4(0.4f, 0.4f, 0.4f, 0.95f);
            if (hover) color *= 1.15f;

            GL.Uniform1(modeLoc, 0);
            GL.Uniform4(colorLoc, color);

            Matrix4 model = MakeRectModel(b.Rect.X, b.Rect.Y, b.Rect.W, b.Rect.H);
            GL.UniformMatrix4(modelLoc, false, ref model);

            GL.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, 0);
        }
    }
    
    //Рисует частицы дыма как полупрозрачные круглые объекты.
    private void DrawSmoke()
    {
        GL.UseProgram(_guiShader);
        GL.BindVertexArray(_quadVao);

        int projLoc = GL.GetUniformLocation(_guiShader, "uProj");
        GL.UniformMatrix4(projLoc, false, ref _proj);
        int modeLoc = GL.GetUniformLocation(_guiShader, "uMode");
        int colorLoc = GL.GetUniformLocation(_guiShader, "uColor");
        int modelLoc = GL.GetUniformLocation(_guiShader, "uModel");

        GL.Uniform1(modeLoc, 1); 

        foreach (var p in _particles)
        {
            float alpha = MathF.Min(0.6f, p.Life * 0.6f);
            Vector4 color = new Vector4(0.85f, 0.85f, 0.9f, alpha);
            GL.Uniform4(colorLoc, color);

            Matrix4 model = MakeRectModel(p.Pos.X - p.Radius, p.Pos.Y - p.Radius, p.Radius * 2f, p.Radius * 2f);
            GL.UniformMatrix4(modelLoc, false, ref model);

            GL.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, 0);
        }
    }
    
    //Обрабатывает нажатия на кнопки, меняя состояние приложения.
    private void HandleButton(int id)
    {
        switch (id)
        {
            case 0: _isRunning = !_isRunning; break;          // Start/Pause
            case 1: _rotationDir = -1f; _isRunning = true; break; // Left
            case 2: _rotationDir =  1f; _isRunning = true; break; // Right
            case 3: _smokeEnabled = !_smokeEnabled; break;    // Smoke toggle
        }
    }
    
    //Пересоздаёт список кнопок, располагая их внизу экрана с отступами.
    private void RebuildButtons()
    {
        _buttons.Clear();

        float pad = 12f;
        float w = 120f, h = 36f;
        float y = Size.Y - h - pad;
        float x = pad;

        _buttons.Add(new Button { Id = 0, Rect = new RectF(x, y, w, h), Color = new Vector4(0.25f, 0.5f, 0.95f, 0.95f) }); x += w + pad;
        _buttons.Add(new Button { Id = 1, Rect = new RectF(x, y, w, h), Color = new Vector4(0.95f, 0.7f, 0.25f, 0.95f) }); x += w + pad;
        _buttons.Add(new Button { Id = 2, Rect = new RectF(x, y, w, h), Color = new Vector4(0.95f, 0.7f, 0.25f, 0.95f) }); x += w + pad;
        _buttons.Add(new Button { Id = 3, Rect = new RectF(x, y, w, h), Color = new Vector4(0.4f, 0.4f, 0.4f, 0.95f) });
    }
    
    //Инициализирует список частиц дыма заданным количеством.
    private void InitParticles(int count)
    {
        _particles.Clear();
        for (int i = 0; i < count; i++) _particles.Add(MakeParticle());
    }
    
    //Создаёт новую частицу с рандомной позицией, скоростью, радиусом и временем жизни.
    private Particle MakeParticle()
    {
        float x = (float)(_rng.NextDouble() * Size.X);
        float y = (float)(_rng.NextDouble() * Size.Y);

        float radius = 20f + (float)_rng.NextDouble() * 30f;
        float vx = (float)(_rng.NextDouble() * 40 - 20);
        float vy = (float)(_rng.NextDouble() * 40 - 20);

        return new Particle
        {
            Pos = new Vector2(x, y),
            Vel = new Vector2(vx, vy),
            Radius = radius,
            Life = 0.4f + (float)_rng.NextDouble() * 0.6f
        };
    }

    //Обновляет состояние частиц — позицию, время жизни и добавляет колебания.
    private void UpdateParticles(float dt)
    {
        for (int i = 0; i < _particles.Count; i++)
        {
            var p = _particles[i];
            p.Pos += p.Vel * dt;
            p.Life -= dt * 0.2f;
            p.Pos.X += MathF.Sin((float)(_time * 2.0 + i)) * 5f * dt;

            if (p.Pos.Y + p.Radius < -10f || p.Life <= 0f)
            {
                p = MakeParticle();
            }
            _particles[i] = p;
        }
    }

    // ===================== Утилиты =====================
    
    //Проверяет, находится ли точка (x, y) внутри прямоугольника r.
    private static bool PointInRect(float x, float y, RectF r)
        => x >= r.X && x <= r.X + r.W && y >= r.Y && y <= r.Y + r.H;
    
    //Создаёт матрицу трансформации модели для прямоугольника с заданной позицией и размерами.
    private static Matrix4 MakeRectModel(float x, float y, float w, float h)
    {
        Matrix4 m = Matrix4.CreateScale(w, h, 1f);
        m.M41 = x; // перенос по X
        m.M42 = y; // перенос по Y
        return m;
    }
    
    //Создаёт шейдерную программу из исходных кодов вершинного и фрагментного шейдеров.
    private static int CreateShaderProgram(string vsSrc, string fsSrc)
    { 
        // Обеспечивает, чтобы исходный код заканчивался переводом строки
        static string EnsureNL(string s) => s.EndsWith("\n") ? s : (s + "\n");
        vsSrc = EnsureNL(vsSrc);
        fsSrc = EnsureNL(fsSrc);

        //// Компиляция вершинного шейдера
        int vs = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vs, vsSrc);
        GL.CompileShader(vs);
        GL.GetShader(vs, ShaderParameter.CompileStatus, out int vStatus);
        if (vStatus == 0) throw new Exception("Vertex shader compile error:\n" + GL.GetShaderInfoLog(vs) + "\nSource:\n" + vsSrc);

        //// Компиляция фрагментного шейдера
        int fs = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fs, fsSrc);
        GL.CompileShader(fs);
        GL.GetShader(fs, ShaderParameter.CompileStatus, out int fStatus);
        if (fStatus == 0) throw new Exception("Fragment shader compile error:\n" + GL.GetShaderInfoLog(fs) + "\nSource:\n" + fsSrc);

        //// Создание программы, привязка шейдеров и линковка
        int prog = GL.CreateProgram();
        GL.AttachShader(prog, vs);
        GL.AttachShader(prog, fs);
        GL.LinkProgram(prog);
        GL.GetProgram(prog, GetProgramParameterName.LinkStatus, out int lStatus);
        if (lStatus == 0) throw new Exception("Program link error:\n" + GL.GetProgramInfoLog(prog));

        //// Очистка шейдеров, т.к. они уже связаны с программой
        GL.DetachShader(prog, vs);
        GL.DetachShader(prog, fs);
        GL.DeleteShader(vs);
        GL.DeleteShader(fs);
        return prog;
    }

    public static void Main()
    {
        using var win = new RgbTriangleWindow();
        win.Run();
    }
}
namespace MockInvestment.Client;
using System.Text.Json;
using MockInvestment.Client.Models;
using MockInvestment.Client.Services;
public partial class LoginForm : Form
{
    private readonly TextBox txtLoginId = new();
    private readonly TextBox txtPassword = new();

    private readonly Button btnLogin = new();
    private readonly Button btnRegister = new();

    private readonly Label lblStatus = new();
    private readonly ApiClient _apiClient;
    private LoginResponse? _session;
    public LoginForm(ApiClient apiClient)
    {
        _apiClient = apiClient;

        InitializeComponent();
        BuildLoginScreen();

        btnLogin.Click += BtnLogin_Click;
        btnRegister.Click += BtnRegister_Click;
    }
    private void BuildLoginScreen()
    {
        Color navy = Color.FromArgb(20, 36, 61);
        Color blue = Color.FromArgb(38, 92, 173);
        Color background = Color.FromArgb(243, 246, 250);

        Text = "모의투자 로그인";
        ClientSize = new Size(520, 560);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        BackColor = background;
        Font = new Font("맑은 고딕", 10F);

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 125,
            BackColor = navy
        };

        var title = new Label
        {
            Text = "MOCK INVESTMENT",
            Location = new Point(35, 30),
            Size = new Size(450, 40),
            Font = new Font("맑은 고딕", 21F, FontStyle.Bold),
            ForeColor = Color.White
        };

        var subtitle = new Label
        {
            Text = "모의 주식투자 및 자산 관리",
            Location = new Point(38, 78),
            Size = new Size(440, 25),
            ForeColor = Color.FromArgb(190, 205, 225)
        };

        header.Controls.Add(title);
        header.Controls.Add(subtitle);

        var card = new Panel
        {
            Location = new Point(30, 150),
            Size = new Size(460, 375),
            BackColor = Color.White
        };

        var loginTitle = new Label
        {
            Text = "로그인",
            Location = new Point(28, 22),
            Size = new Size(400, 30),
            Font = new Font("맑은 고딕", 15F, FontStyle.Bold),
            ForeColor = navy
        };

        var lblLoginId = new Label
        {
            Text = "아이디",
            Location = new Point(30, 70),
            AutoSize = true,
            ForeColor = navy
        };

        txtLoginId.Name = "txtLoginId";
        txtLoginId.Location = new Point(30, 96);
        txtLoginId.Size = new Size(400, 30);
        txtLoginId.PlaceholderText = "아이디를 입력하세요";
        txtLoginId.MaxLength = 50;
        txtLoginId.TabIndex = 0;

        var lblPassword = new Label
        {
            Text = "비밀번호",
            Location = new Point(30, 142),
            AutoSize = true,
            ForeColor = navy
        };

        txtPassword.Name = "txtPassword";
        txtPassword.Location = new Point(30, 168);
        txtPassword.Size = new Size(400, 30);
        txtPassword.PlaceholderText = "비밀번호를 입력하세요";
        txtPassword.MaxLength = 128;
        txtPassword.UseSystemPasswordChar = true;
        txtPassword.TabIndex = 1;

        btnLogin.Name = "btnLogin";
        btnLogin.Text = "로그인";
        btnLogin.Location = new Point(30, 222);
        btnLogin.Size = new Size(250, 42);
        btnLogin.BackColor = blue;
        btnLogin.ForeColor = Color.White;
        btnLogin.FlatStyle = FlatStyle.Flat;
        btnLogin.FlatAppearance.BorderSize = 0;
        btnLogin.UseVisualStyleBackColor = false;
        btnLogin.Cursor = Cursors.Hand;
        btnLogin.TabIndex = 2;

        btnRegister.Name = "btnRegister";
        btnRegister.Text = "회원가입";
        btnRegister.Location = new Point(292, 222);
        btnRegister.Size = new Size(138, 42);
        btnRegister.BackColor = Color.FromArgb(233, 238, 245);
        btnRegister.ForeColor = navy;
        btnRegister.FlatStyle = FlatStyle.Flat;
        btnRegister.FlatAppearance.BorderSize = 0;
        btnRegister.UseVisualStyleBackColor = false;
        btnRegister.Cursor = Cursors.Hand;
        btnRegister.TabIndex = 3;

        lblStatus.Name = "lblStatus";
        lblStatus.Text = string.Empty;
        lblStatus.Location = new Point(30, 285);
        lblStatus.Size = new Size(400, 65);
        lblStatus.AutoSize = false;
        lblStatus.ForeColor = Color.FromArgb(92, 106, 125);
        lblStatus.TabStop = false;

        card.Controls.AddRange(new Control[]
        {
        loginTitle,
        lblLoginId,
        txtLoginId,
        lblPassword,
        txtPassword,
        btnLogin,
        btnRegister,
        lblStatus
        });

        Controls.Add(card);
        Controls.Add(header);

        AcceptButton = btnLogin;
    }

    private async void BtnLogin_Click(
    object? sender,
    EventArgs e)
    {
        string loginId = txtLoginId.Text.Trim();
        string password = txtPassword.Text;

        if (string.IsNullOrWhiteSpace(loginId)
            || string.IsNullOrWhiteSpace(password))
        {
            lblStatus.Text = "아이디와 비밀번호를 입력해주세요.";
            return;
        }

        btnLogin.Enabled = false;
        btnRegister.Enabled = false;
        lblStatus.Text = "로그인 중입니다.";

        _session = null;

        try
        {
            var session = await _apiClient.LoginAsync(
                loginId,
                password);

            // JWT는 화면이나 파일에 출력하지 않고 메모리에 보관
            _session = session;

            txtPassword.Clear();

            lblStatus.Text =
                $"{session.User.Nickname}님, 로그인되었습니다.";
            Hide();

            try
            {
                using var tradingForm =
                    new TradingForm(_apiClient, session);

                tradingForm.ShowDialog();
            }
            finally
            {
                _session = null;

                if (!IsDisposed)
                {
                    Show();
                    txtPassword.Clear();
                    lblStatus.Text = "다시 로그인할 수 있습니다.";
                    txtLoginId.Focus();
                }
            }
        }

        catch (HttpRequestException)
        {
            lblStatus.Text =
                "서버에 연결하거나 요청을 처리하지 못했습니다.";
        }
        catch (OperationCanceledException)
        {
            lblStatus.Text =
                "서버 응답 시간이 초과되었습니다.";
        }
        catch (InvalidOperationException ex)
        {
            lblStatus.Text = ex.Message;
        }
        catch (JsonException)
        {
            lblStatus.Text =
                "서버 응답을 읽지 못했습니다.";
        }
        finally
        {
            btnLogin.Enabled = true;
            btnRegister.Enabled = true;
        }
    }

    private void BtnRegister_Click(
    object? sender,
    EventArgs e)
    {
        using var registerForm = new RegisterForm(_apiClient);

        if (registerForm.ShowDialog(this) == DialogResult.OK)
        {
            txtLoginId.Text = registerForm.RegisteredLoginId;
            txtPassword.Clear();
            txtPassword.Focus();

            lblStatus.Text =
                "회원가입이 완료되었습니다. 비밀번호를 입력해 로그인해주세요.";
        }
    }
}
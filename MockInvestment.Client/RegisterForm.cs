using System.Text.Json;
using MockInvestment.Client.Models;
using MockInvestment.Client.Services;

namespace MockInvestment.Client;

public class RegisterForm : Form
{
    private readonly ApiClient _apiClient;

    private readonly TextBox txtLoginId = new();
    private readonly TextBox txtNickname = new();
    private readonly TextBox txtPassword = new();
    private readonly TextBox txtConfirmPassword = new();

    private readonly Button btnSubmit = new();
    private readonly Label lblStatus = new();

    private bool _submitting;

    public string RegisteredLoginId { get; private set; } =
        string.Empty;

    public RegisterForm(ApiClient apiClient)
    {
        _apiClient = apiClient;

        BuildScreen();

        btnSubmit.Click += BtnSubmit_Click;

        FormClosing += (_, e) =>
        {
            if (_submitting)
            {
                e.Cancel = true;
                lblStatus.Text = "가입 요청을 처리 중입니다. 잠시 기다려주세요.";
            }
        };
    }

    private void BuildScreen()
    {
        Text = "회원가입";
        ClientSize = new Size(500, 480);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        BackColor = Color.White;
        Font = new Font("맑은 고딕", 10F);

        var title = new Label
        {
            Text = "모의투자 회원가입",
            ForeColor = Color.FromArgb(22, 39, 65),
            Font = new Font("맑은 고딕", 17F, FontStyle.Bold),
            Location = new Point(30, 25),
            Size = new Size(440, 40)
        };

        var guide = new Label
        {
            Text = "가입 시 가상 투자금 1억 원이 지급됩니다.",
            ForeColor = Color.DimGray,
            Location = new Point(30, 75),
            Size = new Size(440, 30)
        };

        AddInput("아이디", txtLoginId, 125, 50);
        AddInput("닉네임", txtNickname, 175, 30);
        AddInput("비밀번호", txtPassword, 225, 128);
        AddInput("비밀번호 확인", txtConfirmPassword, 275, 128);

        txtPassword.UseSystemPasswordChar = true;
        txtConfirmPassword.UseSystemPasswordChar = true;

        txtLoginId.TabIndex = 0;
        txtNickname.TabIndex = 1;
        txtPassword.TabIndex = 2;
        txtConfirmPassword.TabIndex = 3;

        btnSubmit.Text = "가입하기";
        btnSubmit.Location = new Point(170, 330);
        btnSubmit.Size = new Size(290, 40);
        btnSubmit.BackColor = Color.FromArgb(22, 39, 65);
        btnSubmit.ForeColor = Color.White;
        btnSubmit.FlatStyle = FlatStyle.Flat;
        btnSubmit.TabIndex = 4;

        lblStatus.Location = new Point(30, 390);
        lblStatus.Size = new Size(440, 75);

        Controls.Add(title);
        Controls.Add(guide);
        Controls.Add(btnSubmit);
        Controls.Add(lblStatus);

        AcceptButton = btnSubmit;
    }

    private void AddInput(
        string title,
        TextBox input,
        int top,
        int maxLength)
    {
        var label = new Label
        {
            Text = title,
            Location = new Point(30, top + 5),
            Size = new Size(135, 25)
        };

        input.Location = new Point(170, top);
        input.Size = new Size(290, 30);
        input.MaxLength = maxLength;

        Controls.Add(label);
        Controls.Add(input);
    }

    private async void BtnSubmit_Click(
        object? sender,
        EventArgs e)
    {
        if (_submitting)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(txtLoginId.Text)
            || string.IsNullOrWhiteSpace(txtNickname.Text)
            || string.IsNullOrWhiteSpace(txtPassword.Text)
            || string.IsNullOrWhiteSpace(txtConfirmPassword.Text))
        {
            lblStatus.Text = "모든 항목을 입력해주세요.";
            return;
        }

        if (txtPassword.Text != txtConfirmPassword.Text)
        {
            lblStatus.Text = "비밀번호가 일치하지 않습니다.";
            return;
        }

        var registration = new RegisterRequest
        {
            LoginId = txtLoginId.Text.Trim(),
            Nickname = txtNickname.Text.Trim(),
            Password = txtPassword.Text,
            ConfirmPassword = txtConfirmPassword.Text
        };

        _submitting = true;
        btnSubmit.Enabled = false;
        lblStatus.Text = "회원가입 처리 중입니다.";

        try
        {
            var result = await _apiClient.RegisterAsync(
                registration);

            RegisteredLoginId = registration.LoginId;

            txtPassword.Clear();
            txtConfirmPassword.Clear();

            MessageBox.Show(
                this,
                $"{result.Nickname}님, 가입되었습니다."
                + Environment.NewLine
                + $"초기 투자금: {result.InitialBalance:N0}원",
                "가입 완료");

            _submitting = false;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (InvalidOperationException ex)
        {
            lblStatus.Text = ex.Message;
        }
        catch (HttpRequestException)
        {
            lblStatus.Text =
                "가입 결과를 확인하지 못했습니다."
                + Environment.NewLine
                + "응답이 유실됐을 수 있으니 로그인 가능 여부를 먼저 확인해주세요.";
        }
        catch (OperationCanceledException)
        {
            lblStatus.Text =
                "응답 시간이 초과되었습니다. 로그인 가능 여부를 먼저 확인해주세요.";
        }
        catch (JsonException)
        {
            lblStatus.Text =
                "가입 응답을 읽지 못했습니다. 로그인 가능 여부를 확인해주세요.";
        }
        finally
        {
            _submitting = false;

            if (!IsDisposed)
            {
                btnSubmit.Enabled = true;
            }
        }
    }
}
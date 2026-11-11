using System.Diagnostics;
using System.Drawing;
using System.Net.Http;
using System.Threading;
using System.Windows.Forms;
using MockInvestment.Client.Services;

namespace MockInvestment.Client;

public sealed class ResearchForm : Form
{
    private readonly ApiClient _api;
    private readonly string _accessToken;

    private readonly TextBox _question = new();
    private readonly Button _askButton = new();
    private readonly Button _cancelButton = new();
    private readonly Button _newConversationButton = new();
    private readonly ComboBox _modeSelector = new();
    private readonly CheckBox _continuePrevious = new();

    private readonly RichTextBox _answer = new();
    private readonly ListView _sources = new();
    private readonly Label _sourceLabel = new();
    private readonly Label _status = new();

    private CancellationTokenSource? _requestCts;
    private Guid? _conversationId;
    private int _turnCount;
    private bool _closing;

    private string CurrentMode =>
        _modeSelector.SelectedIndex == 0
            ? "general"
            : "research";

    public ResearchForm(ApiClient api, string accessToken)
    {
        _api = api;
        _accessToken = accessToken;

        BuildScreen();
        ResetConversation();
    }

    private void BuildScreen()
    {
        Text = "AI 도우미";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(950, 750);
        MinimumSize = new Size(760, 600);
        Font = new Font("맑은 고딕", 10);
        BackColor = Color.FromArgb(243, 246, 251);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 8
        };

        layout.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100));

        layout.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 85));
        layout.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 125));
        layout.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 30));

        var heading = new Label
        {
            Text = "AI 도우미  |  일반 설명 · 자료 조사",
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(22, 39, 62),
            ForeColor = Color.White,
            Font = new Font(
                "맑은 고딕", 14, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0)
        };

        var questionLabel = new Label
        {
            Text = "답변 방식을 선택하고 질문을 입력하세요. (2~400자)",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        _question.Dock = DockStyle.Fill;
        _question.Multiline = true;
        _question.ScrollBars = ScrollBars.Vertical;
        _question.MaxLength = 400;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 5, 0, 0)
        };

        _modeSelector.DropDownStyle =
            ComboBoxStyle.DropDownList;
        _modeSelector.Width = 140;
        _modeSelector.Items.Add("일반 설명");
        _modeSelector.Items.Add("자료 조사(RAG)");

        // 이벤트 연결 전에 기본 선택값 지정
        _modeSelector.SelectedIndex = 0;

        _modeSelector.SelectedIndexChanged += (_, _) =>
        {
            ResetConversation();
        };

        _askButton.Size = new Size(165, 32);
        _askButton.BackColor = Color.FromArgb(38, 88, 166);
        _askButton.ForeColor = Color.White;
        _askButton.FlatStyle = FlatStyle.Flat;
        _askButton.Click += async (_, _) => await AskAsync();

        _cancelButton.Text = "취소";
        _cancelButton.Size = new Size(80, 32);
        _cancelButton.Enabled = false;

        _cancelButton.Click += (_, _) =>
        {
            if (_requestCts is null)
            {
                return;
            }

            _cancelButton.Enabled = false;
            _status.Text = "요청을 취소하고 있습니다.";
            _requestCts.Cancel();
        };

        _newConversationButton.Text = "새 대화";
        _newConversationButton.Size = new Size(100, 32);

        _newConversationButton.Click += (_, _) =>
        {
            ResetConversation();
        };

        buttons.Controls.Add(_modeSelector);
        buttons.Controls.Add(_askButton);
        buttons.Controls.Add(_cancelButton);
        buttons.Controls.Add(_newConversationButton);

        _continuePrevious.Text = "이전 질문과 연결";
        _continuePrevious.AutoSize = true;
        _continuePrevious.Margin = new Padding(10, 6, 0, 0);
        _continuePrevious.Enabled = false;
        _continuePrevious.Checked = false;

        buttons.Controls.Add(_continuePrevious);

        _answer.Dock = DockStyle.Fill;
        _answer.ReadOnly = true;
        _answer.BackColor = Color.White;
        _answer.BorderStyle = BorderStyle.FixedSingle;
        _answer.Font = new Font("맑은 고딕", 11);
        _answer.DetectUrls = false;

        _sourceLabel.Dock = DockStyle.Fill;
        _sourceLabel.TextAlign = ContentAlignment.MiddleLeft;
        _sourceLabel.AutoEllipsis = true;

        _sources.Dock = DockStyle.Fill;
        _sources.View = View.Details;
        _sources.FullRowSelect = true;
        _sources.MultiSelect = false;
        _sources.HideSelection = false;

        _sources.Columns.Add("번호", 55);
        _sources.Columns.Add("자료 제목", 430);
        _sources.Columns.Add("출처", 250);

        _sources.DoubleClick += (_, _) =>
        {
            OpenSelectedSource();
        };

        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.ForeColor = Color.FromArgb(70, 85, 110);
        _status.AutoEllipsis = true;

        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(questionLabel, 0, 1);
        layout.Controls.Add(_question, 0, 2);
        layout.Controls.Add(buttons, 0, 3);
        layout.Controls.Add(_answer, 0, 4);
        layout.Controls.Add(_sourceLabel, 0, 5);
        layout.Controls.Add(_sources, 0, 6);
        layout.Controls.Add(_status, 0, 7);

        Controls.Add(layout);
    }

    private void ResetConversation()
    {
        if (_requestCts is not null)
        {
            return;
        }

        _conversationId = null;
        _turnCount = 0;

        _continuePrevious.Checked = false;
        _continuePrevious.Enabled = false;

        _question.Clear();
        _answer.Clear();
        _sources.Items.Clear();

        bool isGeneral = CurrentMode == "general";

        _askButton.Text = isGeneral
            ? "질문하기"
            : "검색하고 질문하기";

        _question.PlaceholderText = isGeneral
            ? "예: 주식이 무엇인지 두 문장으로 설명해줘."
            : "예: 애플의 최근 분기 매출을 출처와 함께 요약해줘.";

        _sourceLabel.Text = isGeneral
            ? "일반 설명은 웹 검색을 사용하지 않아 검색 출처가 없습니다."
            : "최신 답변의 근거 자료 — 더블클릭하면 원문을 엽니다.";

        _status.Text = isGeneral
            ? "새 대화 · 일반 설명: 검색 없이 개념을 설명합니다."
            : "새 대화 · 자료 조사: 웹 자료를 검색하고 출처를 제공합니다.";

        _question.Focus();
    }

    private async Task AskAsync()
    {
        // 중복 요청 방지
        if (_requestCts is not null || _closing)
        {
            return;
        }

        string question = _question.Text.Trim();
        string mode = CurrentMode;

        bool usePreviousContext = _continuePrevious.Checked
            && _conversationId.HasValue;

        // 두 모드 모두 체크한 경우에만 이전 서버 대화를 사용.
        Guid? requestConversationId =
            !usePreviousContext
                ? null
                : _conversationId;

        if (question.Length < 2 || question.Length > 400)
        {
            MessageBox.Show(
                this,
                "질문은 2~400자로 입력해주세요.",
                "입력 확인",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return;
        }

        using var requestCts = new CancellationTokenSource();
        _requestCts = requestCts;

        _askButton.Enabled = false;
        _modeSelector.Enabled = false;
        _continuePrevious.Enabled = false;
        _newConversationButton.Enabled = false;
        _question.Enabled = false;
        _cancelButton.Enabled = true;

        // 요청 중에는 이전 답변과 출처를 유지
        _status.Text = mode == "general"
            ? "개념 설명을 작성하고 있습니다…"
            : "관련 자료를 검색하고 답변을 작성하고 있습니다…";

        try
        {
            var result = await _api.AskResearchAsync(_accessToken,question,
                requestCts.Token, conversationId: requestConversationId,
                mode: mode, usePreviousContext: usePreviousContext);

            if (_closing
                || IsDisposed
                || requestCts.IsCancellationRequested)
            {
                return;
            }

            if (result.Mode != mode)
            {
                throw new InvalidOperationException(
                    "서버 응답 방식이 선택한 방식과 다릅니다. "
                    + "API 서버의 수정 사항을 확인하고 재실행해주세요.");
            }

            if (result.ConversationId is null
                || result.ConversationId == Guid.Empty)
            {
                throw new InvalidOperationException(
                    "서버에서 대화 번호를 받지 못했습니다. "
                    + "API 서버의 대화 기능을 확인해주세요.");
            }

            _conversationId = result.ConversationId;
            _turnCount++;

            string modeName = mode == "general"
                ? "일반 설명 · 웹 검색 미사용"
                : "자료 조사 · 검색 자료 기반";

            _answer.AppendText(
                $"질문 {_turnCount} [{modeName}]\n"
                + question
                + $"\n\n답변 {_turnCount}\n"
                + result.Answer
                + "\n\n");

            // 목록은 최신 답변의 출처만 표시
            _sources.Items.Clear();

            if (mode == "research")
            {
                bool sourceHeadingAdded = false;

                foreach (var source in result.Sources)
                {
                    if (!Uri.TryCreate(
                            source.Url,
                            UriKind.Absolute,
                            out var uri)
                        || (uri.Scheme != Uri.UriSchemeHttps
                            && uri.Scheme != Uri.UriSchemeHttp)
                        || !string.IsNullOrEmpty(uri.UserInfo))
                    {
                        continue;
                    }

                    if (!sourceHeadingAdded)
                    {
                        _answer.AppendText(
                            $"답변 {_turnCount}의 출처\n");

                        sourceHeadingAdded = true;
                    }

                    // 이전 답변의 출처도 대화 영역에 남김
                    _answer.AppendText(
                        $"[{source.SourceNumber}] {source.Title}\n"
                        + uri.AbsoluteUri
                        + "\n");

                    var item = new ListViewItem(
                        $"[{source.SourceNumber}]");

                    item.SubItems.Add(source.Title);
                    item.SubItems.Add(uri.Host);
                    item.Tag = uri;

                    _sources.Items.Add(item);
                }

                _sourceLabel.Text =
                    $"답변 {_turnCount}의 근거 자료"
                    + " — 더블클릭하면 원문을 엽니다.";
            }
            else
            {
                _sourceLabel.Text =
                    "일반 설명은 웹 검색을 사용하지 않아 검색 출처가 없습니다.";
            }

            _answer.AppendText(
                "\n────────────────────\n\n");

            _answer.SelectionStart = _answer.TextLength;
            _answer.ScrollToCaret();

            _question.Clear();

            // 다음 질문도 사용자가 연결 여부를 직접 선택
            _continuePrevious.Checked = false;

            string generatedTime = result.GeneratedAt
                .ToLocalTime()
                .ToString("HH:mm:ss");

            _status.Text = mode == "general"
                ? $"일반 설명 완료 {generatedTime}"
                    + " · 최신 정보는 자료 조사 모드를 이용해주세요."
                : $"자료 조사 완료 {generatedTime}"
                    + $" · 인용 출처 {_sources.Items.Count}개";
        }
        catch (OperationCanceledException)
        {
            if (!_closing && !IsDisposed)
            {
                _status.Text = requestCts.IsCancellationRequested
                    ? "요청을 취소했습니다. 이전 대화는 유지됩니다."
                    : "응답 시간이 초과되었습니다. 이전 대화는 유지됩니다.";
            }
        }
        catch (Exception ex) when (
            ex is HttpRequestException
                or InvalidOperationException
                or System.Text.Json.JsonException)
        {
            if (!_closing && !IsDisposed)
            {
                _status.Text =
                    "답변을 가져오지 못했습니다. 이전 대화는 유지됩니다.";

                string message = ex is InvalidOperationException
                    ? ex.Message
                    : "서버 연결 또는 응답 처리에 실패했습니다.";

                MessageBox.Show(
                    this,
                    message,
                    "AI 도우미",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        finally
        {
            _requestCts = null;

            if (!_closing && !IsDisposed)
            {
                _askButton.Enabled = true;
                _modeSelector.Enabled = true;

                _continuePrevious.Enabled = _conversationId.HasValue;

                _newConversationButton.Enabled = true;
                _question.Enabled = true;
                _cancelButton.Enabled = false;

                _question.Focus();
            }
        }
    }

    private void OpenSelectedSource()
    {
        if (_sources.SelectedItems.Count == 0
            || _sources.SelectedItems[0].Tag is not Uri uri)
        {
            return;
        }

        if ((uri.Scheme != Uri.UriSchemeHttps
                && uri.Scheme != Uri.UriSchemeHttp)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch
        {
            MessageBox.Show(
                this,
                "기본 브라우저에서 출처를 열지 못했습니다.",
                "출처 열기",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    protected override void OnFormClosing(
        FormClosingEventArgs e)
    {
        base.OnFormClosing(e);

        if (!e.Cancel)
        {
            _closing = true;
            _requestCts?.Cancel();
        }
    }
}
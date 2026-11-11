using System.Text.Json;
using MockInvestment.Client.Models;
using MockInvestment.Client.Services;
using MockInvestment.Client.Forms;

namespace MockInvestment.Client;

public class CompetitionListForm : Form
{
    private readonly ApiClient _apiClient;
    private readonly LoginResponse _session;

    private readonly DataGridView gridCompetitions = new();
    private readonly Label lblStatus = new();
    private readonly Label lblDetail = new();

    private readonly Button btnRefresh = new();
    private readonly Button btnApply = new();
    private readonly Button btnFinalRanking = new();

    private readonly CancellationTokenSource _cts = new();

    private List<Competition> _competitions = new();

    private readonly Dictionary<int, string>
        _myApplicationStatuses = new();

    public CompetitionListForm(
        ApiClient apiClient,
        LoginResponse session)
    {
        _apiClient = apiClient;
        _session = session;

        BuildScreen();

        btnRefresh.Click += async (_, _) =>
            await LoadCompetitionsAsync();

        btnApply.Click += async (_, _) =>
            await ApplyCompetitionAsync();

        btnFinalRanking.Click += (_, _) =>
            OpenFinalRankings();

        gridCompetitions.SelectionChanged += (_, _) =>
        {
            UpdateApplyButton();
        };

        Shown += async (_, _) =>
            await LoadCompetitionsAsync();

        FormClosing += (_, _) =>
            _cts.Cancel();

        FormClosed += (_, _) =>
            _cts.Dispose();
    }

    private void BuildScreen()
    {
        Color navy = Color.FromArgb(20, 36, 61);
        Color blue = Color.FromArgb(38, 92, 173);
        Color background = Color.FromArgb(243, 246, 250);

        Text = "모의투자 대회";
        ClientSize = new Size(1000, 680);
        MinimumSize = new Size(850, 580);
        StartPosition = FormStartPosition.CenterParent;

        BackColor = background;
        Font = new Font("맑은 고딕", 10F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            ColumnCount = 1,
            RowCount = 4,
            BackColor = background
        };

        root.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100));

        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 75));

        root.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));

        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 100));

        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 45));

        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = navy,
            Padding = new Padding(20, 15, 20, 15)
        };

        var title = new Label
        {
            Text = "대회 목록",
            Dock = DockStyle.Left,
            Width = 400,
            ForeColor = Color.White,
            Font = new Font(
                "맑은 고딕",
                15F,
                FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };

        StyleButton(btnRefresh, "새로고침", blue);

        btnRefresh.Dock = DockStyle.Right;
        btnRefresh.Width = 110;
        btnRefresh.Height = 40;

        header.Controls.Add(btnRefresh);
        header.Controls.Add(title);

        gridCompetitions.Dock = DockStyle.Fill;
        gridCompetitions.ReadOnly = true;
        gridCompetitions.AllowUserToAddRows = false;
        gridCompetitions.AllowUserToDeleteRows = false;
        gridCompetitions.MultiSelect = false;
        gridCompetitions.RowHeadersVisible = false;
        gridCompetitions.AutoGenerateColumns = false;

        gridCompetitions.SelectionMode =
            DataGridViewSelectionMode.FullRowSelect;

        gridCompetitions.AutoSizeColumnsMode =
            DataGridViewAutoSizeColumnsMode.Fill;

        gridCompetitions.BackgroundColor = Color.White;
        gridCompetitions.BorderStyle = BorderStyle.None;
        gridCompetitions.EnableHeadersVisualStyles = false;
        gridCompetitions.ColumnHeadersHeight = 40;
        gridCompetitions.RowTemplate.Height = 38;

        gridCompetitions.ColumnHeadersDefaultCellStyle =
            new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(233, 238, 245),
                ForeColor = navy,
                Font = new Font(
                    "맑은 고딕",
                    10F,
                    FontStyle.Bold),
                Padding = new Padding(6, 0, 0, 0)
            };

        gridCompetitions.DefaultCellStyle =
            new DataGridViewCellStyle
            {
                BackColor = Color.White,
                ForeColor = navy,
                SelectionBackColor =
                    Color.FromArgb(221, 234, 252),
                SelectionForeColor = navy,
                Padding = new Padding(6, 0, 0, 0)
            };

        AddColumn(
            "대회 번호",
            nameof(Competition.CompetitionId),
            65);

        AddColumn(
            "대회명",
            nameof(Competition.Title),
            160);

        AddColumn(
            "시작일",
            nameof(Competition.StartDate),
            120,
            "yyyy-MM-dd HH:mm");

        AddColumn(
            "종료일",
            nameof(Competition.EndDate),
            120,
            "yyyy-MM-dd HH:mm");

        AddColumn(
            "초기 투자금",
            nameof(Competition.InitialBalance),
            100,
            "N0");

        AddColumn(
            "상태",
            nameof(Competition.Status),
            75);

        var detailPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(15)
        };

        lblDetail.Dock = DockStyle.Fill;
        lblDetail.ForeColor = navy;

        lblDetail.Text =
            "대회를 선택하면 참가 신청 상태를 확인할 수 있습니다.";

        lblDetail.TextAlign =
            ContentAlignment.MiddleLeft;

        lblDetail.Font =
            new Font("맑은 고딕", 10F);

        detailPanel.Controls.Add(lblDetail);

        var bottom = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = background,
            Padding = new Padding(0, 5, 0, 0)
        };

        StyleButton(btnApply, "참가 신청", blue);

        StyleButton(
            btnFinalRanking,
            "최종 순위",
            Color.FromArgb(75, 90, 110));

        btnFinalRanking.Dock = DockStyle.Right;
        btnFinalRanking.Width = 120;
        btnFinalRanking.Height = 38;
        btnFinalRanking.Enabled = false;

        bottom.Controls.Add(btnFinalRanking);

        btnApply.Dock = DockStyle.Right;
        btnApply.Margin = new Padding(8, 0, 0, 0);
        btnApply.Width = 120;
        btnApply.Height = 38;
        btnApply.Enabled = false;

        bottom.Controls.Add(btnApply);

        lblStatus.Dock = DockStyle.Fill;
        lblStatus.ForeColor =
            Color.FromArgb(92, 106, 125);

        lblStatus.TextAlign =
            ContentAlignment.MiddleLeft;

        bottom.Controls.Add(lblStatus);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(gridCompetitions, 0, 1);
        root.Controls.Add(detailPanel, 0, 2);
        root.Controls.Add(bottom, 0, 3);

        Controls.Add(root);
    }

    private void AddColumn(
        string title,
        string propertyName,
        float fillWeight,
        string? format = null)
    {
        var column = new DataGridViewTextBoxColumn
        {
            HeaderText = title,
            DataPropertyName = propertyName,
            FillWeight = fillWeight,
            SortMode =
                DataGridViewColumnSortMode.NotSortable
        };

        if (!string.IsNullOrWhiteSpace(format))
        {
            column.DefaultCellStyle.Format = format;
        }

        gridCompetitions.Columns.Add(column);
    }

    private void StyleButton(
        Button button,
        string text,
        Color color)
    {
        button.Text = text;
        button.BackColor = color;
        button.ForeColor = Color.White;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.UseVisualStyleBackColor = false;
        button.Cursor = Cursors.Hand;

        button.Font = new Font(
            "맑은 고딕",
            9.5F,
            FontStyle.Bold);
    }

    private async Task LoadCompetitionsAsync()
    {
        if (_cts.IsCancellationRequested)
            return;

        btnRefresh.Enabled = false;
        btnApply.Enabled = false;

        lblStatus.Text =
            "대회 목록과 참가 상태를 조회하고 있습니다.";

        try
        {
            _competitions =
                await _apiClient.GetCompetitionsAsync(
                    _session.AccessToken,
                    _cts.Token);

            if (_cts.IsCancellationRequested)
                return;

            _myApplicationStatuses.Clear();

            foreach (Competition competition in _competitions)
            {
                if (_cts.IsCancellationRequested)
                    return;

                CompetitionApplication? application =
                    await _apiClient.GetMyCompetitionApplicationAsync(
                        _session.AccessToken,
                        competition.CompetitionId,
                        _cts.Token);

                if (application != null)
                {
                    _myApplicationStatuses[
                        competition.CompetitionId] = application.Status;
                }
            }

            if (_cts.IsCancellationRequested)
                return;

            gridCompetitions.DataSource = null;
            gridCompetitions.DataSource = _competitions;

            lblStatus.Text =
                $"총 {_competitions.Count:N0}개의 대회가 조회되었습니다.";

            if (_competitions.Count == 0)
            {
                lblDetail.Text =
                    "등록된 대회가 없습니다.";
            }
            else
            {
                lblDetail.Text =
                    "대회를 선택하면 본인의 참가 신청 상태를 확인할 수 있습니다.";
            }
        }
        catch (OperationCanceledException)
        {
            if (!_cts.IsCancellationRequested)
                lblStatus.Text = "조회 시간이 초과되었습니다.";
        }
        catch (HttpRequestException ex)
        {
            if (!_cts.IsCancellationRequested)
                lblStatus.Text = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            if (!_cts.IsCancellationRequested)
                lblStatus.Text = ex.Message;
        }
        catch (JsonException)
        {
            if (!_cts.IsCancellationRequested)
                lblStatus.Text = "대회 목록 응답을 읽지 못했습니다.";
        }
        finally
        {
            if (!IsDisposed && !_cts.IsCancellationRequested)
            {
                btnRefresh.Enabled = true;
                UpdateApplyButton();
            }
        }
    }

    private string GetMyApplicationStatus(int competitionId)
    {
        if (_myApplicationStatuses.TryGetValue(
            competitionId,
            out string? status))
        {
            return status;
        }

        return "";
    }

    private void UpdateApplyButton()
    {
        Competition? selected =
            gridCompetitions.CurrentRow?.DataBoundItem
            as Competition;

        if (selected == null)
        {
            btnFinalRanking.Enabled = false;
            btnApply.Enabled = false;
            btnApply.Text = "참가 신청";
            return;
        }

        btnFinalRanking.Enabled =
            selected.Status == "종료";

        string applicationStatus =
            GetMyApplicationStatus(selected.CompetitionId);

        string displayStatus;

        if (applicationStatus == "대기")
        {
            displayStatus = "승인 대기";
            btnApply.Text = "승인 대기";
            btnApply.Enabled = false;
        }
        else if (applicationStatus == "승인")
        {
            displayStatus = "참가 중";
            btnApply.Text = "참가 중";
            btnApply.Enabled = false;
        }
        else if (applicationStatus == "거절")
        {
            displayStatus = "참가 거절 - 재신청 가능";
            btnApply.Text = "재신청";
            btnApply.Enabled = selected.Status == "준비";
        }
        else
        {
            displayStatus = "신청 내역 없음";
            btnApply.Text = "참가 신청";
            btnApply.Enabled = selected.Status == "준비";
        }

        lblDetail.Text =
            $"대회명: {selected.Title}"
            + Environment.NewLine
            + $"대회 상태: {selected.Status}"
            + Environment.NewLine
            + $"내 참가 상태: {displayStatus}";
    }

    private void OpenFinalRankings()
    {
        Competition? selected =
            gridCompetitions.CurrentRow?.DataBoundItem
            as Competition;

        if (selected == null || selected.Status != "종료")
        {
            MessageBox.Show(
                this,
                "최종 순위가 확정된 대회를 선택해주세요.");

            return;
        }

        using var form = new CompetitionFinalRankingForm(
            _apiClient,
            _session.AccessToken,
            selected.CompetitionId,
            selected.Title);

        form.ShowDialog(this);
    }

    private async Task ApplyCompetitionAsync()
    {
        Competition? selected =
            gridCompetitions.CurrentRow?.DataBoundItem
            as Competition;

        if (selected == null)
        {
            MessageBox.Show(
                this,
                "참가 신청할 대회를 선택해주세요.",
                "알림",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return;
        }

        if (selected.Status != "준비")
        {
            MessageBox.Show(
                this,
                "현재 참가 신청할 수 없는 대회입니다.");

            return;
        }

        string applicationStatus =
            GetMyApplicationStatus(selected.CompetitionId);

        if (applicationStatus == "대기"
            || applicationStatus == "승인")
        {
            MessageBox.Show(
                this,
                applicationStatus == "승인"
                    ? "이미 참가 중인 대회입니다."
                    : "이미 참가 신청이 대기 중입니다.");

            UpdateApplyButton();
            return;
        }

        var result = MessageBox.Show(
            this,
            $"'{selected.Title}' 대회에 참가 신청하시겠습니까?",
            "대회 참가 신청",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
            return;

        btnApply.Enabled = false;
        btnRefresh.Enabled = false;

        try
        {
            await _apiClient.ApplyCompetitionAsync(
                _session.AccessToken,
                selected.CompetitionId,
                _cts.Token);

            if (_cts.IsCancellationRequested)
                return;

            _myApplicationStatuses[selected.CompetitionId] = "대기";

            MessageBox.Show(
                this,
                "참가 신청이 완료되었습니다.\n관리자 승인 후 대회에 참가할 수 있습니다.",
                "참가 신청 완료",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            lblStatus.Text =
                $"{selected.Title} 참가 신청 완료 - 관리자 승인 대기";

            UpdateApplyButton();
        }
        catch (OperationCanceledException)
        {
            if (!_cts.IsCancellationRequested)
            {
                MessageBox.Show(
                    this,
                    "참가 신청 시간이 초과되었습니다.");
            }
        }
        catch (HttpRequestException ex)
        {
            if (!_cts.IsCancellationRequested)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "참가 신청 실패",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        catch (InvalidOperationException ex)
        {
            if (!_cts.IsCancellationRequested)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "참가 신청 실패",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            if (!_cts.IsCancellationRequested)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "참가 신청 실패",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        finally
        {
            if (!IsDisposed && !_cts.IsCancellationRequested)
            {
                btnRefresh.Enabled = true;
                UpdateApplyButton();
            }
        }
    }
}
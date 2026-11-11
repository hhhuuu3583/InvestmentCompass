
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;

using MockInvestment.Client.Models;
using MockInvestment.Client.Services;

namespace MockInvestment.Client.Forms;

public class AdminCompetitionForm : Form
{
    private readonly ApiClient _apiClient;
    private readonly LoginResponse _session;

    private readonly DataGridView gridCompetitions = new();
    private readonly DataGridView gridApplications = new();

    private readonly TextBox txtTitle = new();

    private readonly DateTimePicker dtpStartDate = new()
    {
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "yyyy-MM-dd HH:mm",
        ShowUpDown = true
    };

    private readonly DateTimePicker dtpEndDate = new()
    {
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "yyyy-MM-dd HH:mm",
        ShowUpDown = true
    };

    private readonly NumericUpDown nudInitialBalance = new()
    {
        Minimum = 1,
        Maximum = 99999999999999999.99m,
        DecimalPlaces = 2,
        ThousandsSeparator = true,
        Value = 100000000
    };

    private readonly Label lblStatus = new();
    private readonly Label lblApplicationStatus = new();

    private readonly Button btnCreate = new();
    private readonly Button btnUpdate = new();
    private readonly Button btnDelete = new();
    private readonly Button btnStart = new();
    private readonly Button btnEnd = new();
    private readonly Button btnFinalRanking = new();
    private readonly Button btnCancel = new();
    private readonly Button btnRefresh = new();
    private readonly Button btnClear = new();

    private readonly Button btnRefreshApplications = new();
    private readonly Button btnApproveApplication = new();
    private readonly Button btnRejectApplication = new();

    private List<Competition> _competitions = new();

    private int _selectedCompetitionId;
    private int _selectedApplicationId;

    private bool _suppressCompetitionSelection;

    public AdminCompetitionForm(
        ApiClient apiClient,
        LoginResponse session)
    {
        _apiClient = apiClient;
        _session = session;

        if (!string.Equals(
                _session.User.Role,
                "Admin",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                "관리자만 대회 관리 화면에 접근할 수 있습니다.");
        }

        BuildScreen();

        Shown += async (_, _) =>
        {
            await LoadCompetitionsAsync();
        };

        gridCompetitions.SelectionChanged +=
            GridCompetitions_SelectionChanged;

        gridApplications.SelectionChanged +=
            GridApplications_SelectionChanged;

        btnCreate.Click += async (_, _) =>
            await CreateCompetitionAsync();

        btnUpdate.Click += async (_, _) =>
            await UpdateCompetitionAsync();

        btnDelete.Click += async (_, _) =>
            await DeleteCompetitionAsync();

        btnStart.Click += async (_, _) =>
            await StartCompetitionAsync();

        btnEnd.Click += async (_, _) =>
            await EndCompetitionAsync();

        btnFinalRanking.Click += (_, _) =>
            OpenFinalRankings();

        btnCancel.Click += async (_, _) =>
            await CancelCompetitionAsync();

        btnRefresh.Click += async (_, _) =>
            await LoadCompetitionsAsync();

        btnClear.Click += (_, _) =>
            ClearInput();

        btnRefreshApplications.Click += async (_, _) =>
            await LoadApplicationsAsync();

        btnApproveApplication.Click += async (_, _) =>
            await ApproveApplicationAsync();

        btnRejectApplication.Click += async (_, _) =>
            await RejectApplicationAsync();
    }

    private void BuildScreen()
    {
        Text = "관리자 - 대회 관리";
        Size = new Size(1150, 900);
        MinimumSize = new Size(1000, 750);
        StartPosition = FormStartPosition.CenterParent;

        Font = new Font("맑은 고딕", 10F);
        BackColor = Color.FromArgb(243, 246, 250);

        // 전체 화면
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 4
        };

        root.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100));

        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 310));

        root.RowStyles.Add(
            new RowStyle(SizeType.Percent, 45));

        root.RowStyles.Add(
            new RowStyle(SizeType.Percent, 55));

        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 35));

        // =====================================
        // 1. 대회 입력 및 관리 버튼 영역
        // =====================================

        var inputPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            BackColor = Color.White,
            Padding = new Padding(15)
        };

        inputPanel.ColumnStyles.Add(
            new ColumnStyle(SizeType.Absolute, 140));

        inputPanel.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100));

        for (int i = 0; i < 4; i++)
        {
            inputPanel.RowStyles.Add(
                new RowStyle(SizeType.Absolute, 42));
        }

        inputPanel.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));

        AddInputRow(inputPanel, 0, "대회 이름", txtTitle);
        AddInputRow(inputPanel, 1, "시작 날짜", dtpStartDate);
        AddInputRow(inputPanel, 2, "종료 날짜", dtpEndDate);
        AddInputRow(inputPanel, 3, "초기 투자금", nudInitialBalance);

        // 관리 버튼 8개를 4개씩 2줄로 배치
        var actionPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            Margin = new Padding(0, 5, 0, 0),
            BackColor = Color.White
        };

        for (int i = 0; i < 4; i++)
        {
            actionPanel.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 25));
        }

        actionPanel.RowStyles.Add(
            new RowStyle(SizeType.Percent, 50));

        actionPanel.RowStyles.Add(
            new RowStyle(SizeType.Percent, 50));

        StyleButton(btnCreate, "대회 등록");
        StyleButton(btnUpdate, "수정");
        StyleButton(btnDelete, "삭제");
        StyleButton(btnStart, "대회 시작");

        StyleButton(btnEnd, "대회 종료");
        StyleButton(btnFinalRanking, "최종 순위 조회");
        StyleButton(btnCancel, "대회 취소");
        StyleButton(btnClear, "입력 초기화");

        Button[] actionButtons =
        {
            btnCreate,
            btnUpdate,
            btnDelete,
            btnStart,
            btnEnd,
            btnFinalRanking,
            btnCancel,
            btnClear
        };

        for (int i = 0; i < actionButtons.Length; i++)
        {
            actionButtons[i].Dock = DockStyle.Fill;
            actionButtons[i].Margin = new Padding(4);

            actionPanel.Controls.Add(
                actionButtons[i],
                i % 4,
                i / 4);
        }

        inputPanel.Controls.Add(actionPanel, 1, 4);

        // =====================================
        // 2. 대회 목록 영역
        // =====================================

        var competitionListPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.White,
            Padding = new Padding(0)
        };

        competitionListPanel.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100));

        competitionListPanel.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 48));

        competitionListPanel.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));

        // 대회 목록 제목 및 새로고침 버튼
        var competitionListHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.White,
            Padding = new Padding(12, 5, 8, 5)
        };

        competitionListHeader.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100));

        competitionListHeader.ColumnStyles.Add(
            new ColumnStyle(SizeType.Absolute, 135));

        competitionListHeader.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));

        var competitionListTitle = new Label
        {
            Text = "대회 목록",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(
                "맑은 고딕",
                11F,
                FontStyle.Bold),
            ForeColor = Color.FromArgb(20, 36, 61)
        };

        StyleButton(btnRefresh, "목록 새로고침");

        btnRefresh.Dock = DockStyle.Fill;
        btnRefresh.Margin = new Padding(3, 2, 3, 2);

        competitionListHeader.Controls.Add(
            competitionListTitle,
            0,
            0);

        competitionListHeader.Controls.Add(
            btnRefresh,
            1,
            0);

        // =====================================
        // 3. 대회 목록 그리드
        // =====================================

        gridCompetitions.Dock = DockStyle.Fill;
        gridCompetitions.ReadOnly = true;
        gridCompetitions.AllowUserToAddRows = false;
        gridCompetitions.AllowUserToDeleteRows = false;
        gridCompetitions.MultiSelect = false;

        gridCompetitions.SelectionMode =
            DataGridViewSelectionMode.FullRowSelect;

        gridCompetitions.AutoGenerateColumns = false;

        gridCompetitions.AutoSizeColumnsMode =
            DataGridViewAutoSizeColumnsMode.Fill;

        gridCompetitions.RowHeadersVisible = false;
        gridCompetitions.BackgroundColor = Color.White;
        gridCompetitions.BorderStyle = BorderStyle.None;
        gridCompetitions.EnableHeadersVisualStyles = false;
        gridCompetitions.RowTemplate.Height = 32;

        gridCompetitions.ColumnHeadersDefaultCellStyle =
            new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(233, 238, 245),
                ForeColor = Color.FromArgb(20, 36, 61),
                Font = new Font(
                    "맑은 고딕",
                    9.5F,
                    FontStyle.Bold),
                Alignment =
                    DataGridViewContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 0, 0)
            };

        gridCompetitions.DefaultCellStyle =
            new DataGridViewCellStyle
            {
                SelectionBackColor =
                    Color.FromArgb(221, 234, 252),

                SelectionForeColor =
                    Color.FromArgb(20, 36, 61),

                Alignment =
                    DataGridViewContentAlignment.MiddleLeft,

                Padding = new Padding(6, 0, 0, 0)
            };

        AddGridColumn(
            "대회 번호",
            nameof(Competition.CompetitionId));

        AddGridColumn(
            "대회 이름",
            nameof(Competition.Title));

        AddGridColumn(
            "시작 날짜",
            nameof(Competition.StartDate));

        AddGridColumn(
            "종료 날짜",
            nameof(Competition.EndDate));

        AddGridColumn(
            "초기 투자금",
            nameof(Competition.InitialBalance));

        AddGridColumn(
            "상태",
            nameof(Competition.Status));

        gridCompetitions.Columns[0].FillWeight = 65;
        gridCompetitions.Columns[1].FillWeight = 150;
        gridCompetitions.Columns[2].FillWeight = 120;
        gridCompetitions.Columns[3].FillWeight = 120;
        gridCompetitions.Columns[4].FillWeight = 110;
        gridCompetitions.Columns[5].FillWeight = 70;

        gridCompetitions.Columns[2].DefaultCellStyle.Format =
            "yyyy-MM-dd HH:mm";

        gridCompetitions.Columns[3].DefaultCellStyle.Format =
            "yyyy-MM-dd HH:mm";

        gridCompetitions.Columns[4].DefaultCellStyle.Format =
            "N0";

        competitionListPanel.Controls.Add(
            competitionListHeader,
            0,
            0);

        competitionListPanel.Controls.Add(
            gridCompetitions,
            0,
            1);

        // =====================================
        // 4. 참가 신청자 관리 영역
        // =====================================

        var applicationPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.White,
            Padding = new Padding(8)
        };

        applicationPanel.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100));

        applicationPanel.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 48));

        applicationPanel.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));

        var applicationHeader = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(5),
            Margin = Padding.Empty
        };

        var applicationTitle = new Label
        {
            Text = "참가 신청자 관리",
            AutoSize = true,
            Font = new Font("맑은 고딕", 11F, FontStyle.Bold),
            ForeColor = Color.FromArgb(20, 36, 61),
            Dock = DockStyle.Left,
            TextAlign = ContentAlignment.MiddleLeft
        };

        lblApplicationStatus.Text = "대회를 선택하면 신청자 목록이 표시됩니다.";
        lblApplicationStatus.AutoSize = true;
        lblApplicationStatus.ForeColor = Color.FromArgb(92, 106, 125);
        lblApplicationStatus.Font = new Font("맑은 고딕", 9F);
        lblApplicationStatus.Dock = DockStyle.Left;
        lblApplicationStatus.TextAlign = ContentAlignment.MiddleLeft;
        lblApplicationStatus.Padding = new Padding(15, 0, 0, 0);

        StyleButton(btnApproveApplication, "참가 승인");
        StyleButton(btnRejectApplication, "참가 거절");
        StyleButton(btnRefreshApplications, "신청자 새로고침");

        btnApproveApplication.Size = new Size(100, 34);
        btnRejectApplication.Size = new Size(100, 34);
        btnRefreshApplications.Size = new Size(130, 34);

        btnApproveApplication.BackColor = Color.FromArgb(35, 145, 95);
        btnRejectApplication.BackColor = Color.FromArgb(190, 65, 65);

        // 버튼 3개만 우측 정렬로 담을 패널 생성
        var rightButtonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.RightToLeft, // 오른쪽부터 왼쪽으로 쌓임
            AutoSize = true,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        // RightToLeft이므로 넣는 순서가 [오른쪽 끝 -> 왼쪽]이 됩니다.
        rightButtonPanel.Controls.Add(btnRefreshApplications);
        rightButtonPanel.Controls.Add(btnRejectApplication);
        rightButtonPanel.Controls.Add(btnApproveApplication);

        applicationHeader.Controls.Add(rightButtonPanel);
        applicationHeader.Controls.Add(lblApplicationStatus);
        applicationHeader.Controls.Add(applicationTitle);

        // 참가 신청자 그리드
        gridApplications.Dock = DockStyle.Fill;
        gridApplications.ReadOnly = true;
        gridApplications.AllowUserToAddRows = false;
        gridApplications.AllowUserToDeleteRows = false;
        gridApplications.MultiSelect = false;

        gridApplications.SelectionMode =
            DataGridViewSelectionMode.FullRowSelect;

        gridApplications.AutoGenerateColumns = false;

        gridApplications.AutoSizeColumnsMode =
            DataGridViewAutoSizeColumnsMode.Fill;

        gridApplications.RowHeadersVisible = false;
        gridApplications.BackgroundColor = Color.White;
        gridApplications.BorderStyle = BorderStyle.None;
        gridApplications.EnableHeadersVisualStyles = false;
        gridApplications.RowTemplate.Height = 32;

        gridApplications.ColumnHeadersDefaultCellStyle =
            new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(233, 238, 245),
                ForeColor = Color.FromArgb(20, 36, 61),
                Font = new Font(
                    "맑은 고딕",
                    9.5F,
                    FontStyle.Bold),
                Alignment =
                    DataGridViewContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 0, 0)
            };

        gridApplications.DefaultCellStyle =
            new DataGridViewCellStyle
            {
                SelectionBackColor =
                    Color.FromArgb(221, 234, 252),

                SelectionForeColor =
                    Color.FromArgb(20, 36, 61),

                Alignment =
                    DataGridViewContentAlignment.MiddleLeft,

                Padding = new Padding(6, 0, 0, 0)
            };

        gridApplications.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                HeaderText = "신청 번호",
                DataPropertyName =
                    nameof(CompetitionApplication.ApplicationId),
                FillWeight = 65
            });

        gridApplications.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                HeaderText = "아이디",
                DataPropertyName =
                    nameof(CompetitionApplication.LoginId),
                FillWeight = 100
            });

        gridApplications.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                HeaderText = "닉네임",
                DataPropertyName =
                    nameof(CompetitionApplication.Nickname),
                FillWeight = 100
            });

        gridApplications.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                HeaderText = "신청 상태",
                DataPropertyName =
                    nameof(CompetitionApplication.Status),
                FillWeight = 75
            });

        gridApplications.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                HeaderText = "신청 날짜",
                DataPropertyName =
                    nameof(CompetitionApplication.AppliedAt),
                FillWeight = 120,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Format = "yyyy-MM-dd HH:mm"
                }
            });

        applicationPanel.Controls.Add(
            applicationHeader,
            0,
            0);

        applicationPanel.Controls.Add(
            gridApplications,
            0,
            1);

        // =====================================
        // 5. 하단 상태 표시
        // =====================================

        lblStatus.Dock = DockStyle.Fill;

        lblStatus.Text =
            "대회 목록을 불러올 준비가 되었습니다.";

        lblStatus.ForeColor =
            Color.FromArgb(92, 106, 125);

        lblStatus.Padding =
            new Padding(0, 8, 0, 0);

        // 전체 화면에 배치
        root.Controls.Add(inputPanel, 0, 0);
        root.Controls.Add(competitionListPanel, 0, 1);
        root.Controls.Add(applicationPanel, 0, 2);
        root.Controls.Add(lblStatus, 0, 3);

        Controls.Add(root);

        UpdateActionButtons();
        UpdateApplicationButtons();
    }

    private void AddInputRow(
        TableLayoutPanel panel,
        int row,
        string title,
        Control control)
    {
        var label = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(20, 36, 61),
            Font = new Font(
                "맑은 고딕",
                10F,
                FontStyle.Bold)
        };

        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(3, 6, 3, 6);

        panel.Controls.Add(label, 0, row);
        panel.Controls.Add(control, 1, row);
    }

    private void AddGridColumn(
        string title,
        string propertyName)
    {
        gridCompetitions.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                HeaderText = title,
                DataPropertyName = propertyName,
                SortMode =
                    DataGridViewColumnSortMode.NotSortable
            });
    }

    private void StyleButton(
        Button button,
        string text)
    {
        button.Text = text;
        button.Size = new Size(112, 38);
        button.Margin = new Padding(0, 0, 8, 0);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;

        button.BackColor =
            Color.FromArgb(38, 92, 173);

        button.ForeColor = Color.White;
        button.Cursor = Cursors.Hand;

        button.Font = new Font(
            "맑은 고딕",
            9.5F,
            FontStyle.Bold);
    }

    private async Task LoadCompetitionsAsync()
    {
        try
        {
            SetBusy(true);

            lblStatus.Text =
                "대회 목록을 조회하고 있습니다.";

            _suppressCompetitionSelection = true;

            _competitions =
                await _apiClient.GetCompetitionsAsync(
                    _session.AccessToken);

            // 대회 번호 내림차순
            _competitions = _competitions
                .OrderByDescending(x => x.CompetitionId)
                .ToList();

            gridCompetitions.DataSource = null;
            gridCompetitions.DataSource = _competitions;

            gridCompetitions.ClearSelection();
            gridCompetitions.CurrentCell = null;

            _selectedCompetitionId = 0;
            _selectedApplicationId = 0;

            gridApplications.DataSource = null;

            lblApplicationStatus.Text =
                "대회를 선택하면 신청자 목록이 표시됩니다.";

            lblStatus.Text =
                $"전체 대회 {_competitions.Count:N0}개";

            ClearInput();
        }
        catch (Exception ex) when (
            ex is HttpRequestException
            || ex is InvalidOperationException
            || ex is TaskCanceledException)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "대회 조회 오류",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            lblStatus.Text =
                "대회 목록 조회에 실패했습니다.";
        }
        finally
        {
            _suppressCompetitionSelection = false;
            SetBusy(false);
        }
    }

    private async void GridCompetitions_SelectionChanged(
        object? sender,
        EventArgs e)
    {
        if (_suppressCompetitionSelection)
            return;

        Competition? competition =
            gridCompetitions.CurrentRow?.DataBoundItem
            as Competition;

        if (competition == null)
        {
            _selectedCompetitionId = 0;
            _selectedApplicationId = 0;

            gridApplications.DataSource = null;

            lblApplicationStatus.Text =
                "대회를 선택하면 신청자 목록이 표시됩니다.";

            UpdateActionButtons();
            UpdateApplicationButtons();

            return;
        }

        _selectedCompetitionId =
            competition.CompetitionId;

        txtTitle.Text = competition.Title;

        dtpStartDate.Value =
            ClampDate(competition.StartDate);

        dtpEndDate.Value =
            ClampDate(competition.EndDate);

        nudInitialBalance.Value = Math.Min(
            nudInitialBalance.Maximum,
            Math.Max(
                nudInitialBalance.Minimum,
                competition.InitialBalance));

        UpdateActionButtons();

        await LoadApplicationsAsync();
    }

    private DateTime ClampDate(DateTime date)
    {
        if (date < dtpStartDate.MinDate)
            return dtpStartDate.MinDate;

        if (date > dtpStartDate.MaxDate)
            return dtpStartDate.MaxDate;

        return date;
    }

    private async Task CreateCompetitionAsync()
    {
        if (!ValidateInput())
            return;

        var request = CreateRequest();

        try
        {
            SetBusy(true);

            await _apiClient.CreateCompetitionAsync(
                _session.AccessToken,
                request);

            MessageBox.Show(
                this,
                "대회가 등록되었습니다.",
                "등록 완료",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            await LoadCompetitionsAsync();
        }
        catch (Exception ex) when (
            ex is HttpRequestException
            || ex is InvalidOperationException
            || ex is TaskCanceledException)
        {
            ShowError(ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task UpdateCompetitionAsync()
    {
        if (!CheckSelected())
            return;

        if (!ValidateInput())
            return;

        if (!Confirm(
            "선택한 대회 정보를 수정하시겠습니까?"))
        {
            return;
        }

        try
        {
            SetBusy(true);

            await _apiClient.UpdateCompetitionAsync(
                _session.AccessToken,
                _selectedCompetitionId,
                CreateRequest());

            MessageBox.Show(
                this,
                "대회 정보가 수정되었습니다.");

            await LoadCompetitionsAsync();
        }
        catch (Exception ex) when (
            ex is HttpRequestException
            || ex is InvalidOperationException
            || ex is TaskCanceledException)
        {
            ShowError(ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task DeleteCompetitionAsync()
    {
        if (!CheckSelected())
            return;

        if (!Confirm(
            "선택한 대회를 삭제하시겠습니까?\n참가자가 있는 대회는 삭제할 수 없습니다."))
        {
            return;
        }

        try
        {
            SetBusy(true);

            await _apiClient.DeleteCompetitionAsync(
                _session.AccessToken,
                _selectedCompetitionId);

            MessageBox.Show(
                this,
                "대회가 삭제되었습니다.");

            await LoadCompetitionsAsync();
        }
        catch (Exception ex) when (
            ex is HttpRequestException
            || ex is InvalidOperationException
            || ex is TaskCanceledException)
        {
            ShowError(ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task StartCompetitionAsync()
    {
        if (!CheckSelected())
            return;

        if (!Confirm(
            "선택한 대회를 시작하시겠습니까?"))
        {
            return;
        }

        try
        {
            SetBusy(true);

            await _apiClient.StartCompetitionAsync(
                _session.AccessToken,
                _selectedCompetitionId);

            MessageBox.Show(
                this,
                "대회가 시작되었습니다.");

            await LoadCompetitionsAsync();
        }
        catch (Exception ex) when (
            ex is HttpRequestException
            || ex is InvalidOperationException
            || ex is TaskCanceledException)
        {
            ShowError(ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OpenFinalRankings()
    {
        if (_selectedCompetitionId <= 0)
        {
            MessageBox.Show(this, "대회를 선택해주세요.");
            return;
        }

        Competition? selected = _competitions.FirstOrDefault(
            x => x.CompetitionId == _selectedCompetitionId);

        if (selected == null || selected.Status != "종료")
        {
            MessageBox.Show(
                this,
                "종료된 대회만 최종 순위를 조회할 수 있습니다.");
            return;
        }

        using var form = new CompetitionFinalRankingForm(
            _apiClient,
            _session.AccessToken,
            selected.CompetitionId,
            selected.Title);

        form.ShowDialog(this);
    }

    private async Task EndCompetitionAsync()
    {
        if (!CheckSelected())
            return;

        if (!Confirm(
            "선택한 대회를 종료하고 최종 순위를 저장하시겠습니까?"))
        {
            return;
        }

        try
        {
            SetBusy(true);

            await _apiClient.EndCompetitionAsync(
                _session.AccessToken,
                _selectedCompetitionId);

            MessageBox.Show(
                this,
                "대회가 종료되고 최종 순위가 저장되었습니다.");

            await LoadCompetitionsAsync();
        }
        catch (Exception ex) when (
            ex is HttpRequestException
            || ex is InvalidOperationException
            || ex is TaskCanceledException)
        {
            ShowError(ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task CancelCompetitionAsync()
    {
        if (!CheckSelected())
            return;

        if (!Confirm(
            "선택한 대회를 취소하시겠습니까?"))
        {
            return;
        }

        try
        {
            SetBusy(true);

            await _apiClient.CancelCompetitionAsync(
                _session.AccessToken,
                _selectedCompetitionId);

            MessageBox.Show(
                this,
                "대회가 취소되었습니다.");

            await LoadCompetitionsAsync();
        }
        catch (Exception ex) when (
            ex is HttpRequestException
            || ex is InvalidOperationException
            || ex is TaskCanceledException)
        {
            ShowError(ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task LoadApplicationsAsync()
    {
        if (_selectedCompetitionId <= 0)
        {
            gridApplications.DataSource = null;

            lblApplicationStatus.Text =
                "대회를 먼저 선택해주세요.";

            UpdateApplicationButtons();

            return;
        }

        try
        {
            btnRefreshApplications.Enabled = false;
            btnApproveApplication.Enabled = false;
            btnRejectApplication.Enabled = false;

            lblApplicationStatus.Text =
                "신청자 목록 조회 중...";

            List<CompetitionApplication> applications =
                await _apiClient.GetCompetitionApplicationsAsync(
                    _session.AccessToken,
                    _selectedCompetitionId);

            gridApplications.DataSource = null;
            gridApplications.DataSource = applications;

            gridApplications.ClearSelection();
            gridApplications.CurrentCell = null;

            _selectedApplicationId = 0;

            int waitingCount = applications.Count(
                x => x.Status == "대기");

            lblApplicationStatus.Text =
                $"전체 {applications.Count}명 / 승인 대기 {waitingCount}명";
        }
        catch (Exception ex) when (
            ex is HttpRequestException
            || ex is InvalidOperationException
            || ex is TaskCanceledException)
        {
            lblApplicationStatus.Text =
                "신청자 조회 실패";

            MessageBox.Show(
                this,
                ex.Message,
                "신청자 조회 오류",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            UpdateApplicationButtons();
        }
    }

    private void GridApplications_SelectionChanged(
        object? sender,
        EventArgs e)
    {
        CompetitionApplication? application =
            gridApplications.CurrentRow?.DataBoundItem
            as CompetitionApplication;

        if (application == null)
        {
            _selectedApplicationId = 0;
        }
        else
        {
            _selectedApplicationId =
                application.ApplicationId;
        }

        UpdateApplicationButtons();
    }

    private async Task ApproveApplicationAsync()
    {
        if (!CheckSelectedApplication())
            return;

        CompetitionApplication? application =
            gridApplications.CurrentRow?.DataBoundItem
            as CompetitionApplication;

        if (application == null
            || application.Status != "대기")
        {
            MessageBox.Show(
                this,
                "승인 대기 중인 신청자만 승인할 수 있습니다.");

            return;
        }

        if (!Confirm(
            $"{application.Nickname} 회원의 참가 신청을 승인하시겠습니까?"))
        {
            return;
        }

        try
        {
            SetBusy(true);

            await _apiClient.ApproveCompetitionApplicationAsync(
                _session.AccessToken,
                _selectedCompetitionId,
                _selectedApplicationId);

            MessageBox.Show(
                this,
                "참가 신청을 승인했습니다.",
                "승인 완료",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            await LoadApplicationsAsync();
        }
        catch (Exception ex) when (
            ex is HttpRequestException
            || ex is InvalidOperationException
            || ex is TaskCanceledException)
        {
            ShowError(ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RejectApplicationAsync()
    {
        if (!CheckSelectedApplication())
            return;

        CompetitionApplication? application =
            gridApplications.CurrentRow?.DataBoundItem
            as CompetitionApplication;

        if (application == null
            || application.Status != "대기")
        {
            MessageBox.Show(
                this,
                "승인 대기 중인 신청자만 거절할 수 있습니다.");

            return;
        }

        if (!Confirm(
            $"{application.Nickname} 회원의 참가 신청을 거절하시겠습니까?"))
        {
            return;
        }

        try
        {
            SetBusy(true);

            await _apiClient.RejectCompetitionApplicationAsync(
                _session.AccessToken,
                _selectedCompetitionId,
                _selectedApplicationId);

            MessageBox.Show(
                this,
                "참가 신청을 거절했습니다.",
                "거절 완료",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            await LoadApplicationsAsync();
        }
        catch (Exception ex) when (
            ex is HttpRequestException
            || ex is InvalidOperationException
            || ex is TaskCanceledException)
        {
            ShowError(ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private bool CheckSelectedApplication()
    {
        if (_selectedCompetitionId <= 0)
        {
            MessageBox.Show(
                this,
                "대회를 먼저 선택해주세요.");

            return false;
        }

        if (_selectedApplicationId <= 0)
        {
            MessageBox.Show(
                this,
                "신청자를 먼저 선택해주세요.");

            return false;
        }

        return true;
    }

    private void UpdateApplicationButtons()
    {
        CompetitionApplication? application =
            gridApplications.CurrentRow?.DataBoundItem
            as CompetitionApplication;

        bool canProcess =
            application != null
            && application.Status == "대기"
            && _selectedCompetitionId > 0;

        btnApproveApplication.Enabled = canProcess;
        btnRejectApplication.Enabled = canProcess;

        btnRefreshApplications.Enabled =
            _selectedCompetitionId > 0;
    }

    private CompetitionRequest CreateRequest()
    {
        return new CompetitionRequest
        {
            Title = txtTitle.Text.Trim(),
            StartDate = dtpStartDate.Value,
            EndDate = dtpEndDate.Value,
            InitialBalance = nudInitialBalance.Value
        };
    }

    private bool ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(txtTitle.Text))
        {
            MessageBox.Show(
                this,
                "대회 이름을 입력해주세요.");

            txtTitle.Focus();

            return false;
        }

        if (dtpStartDate.Value >= dtpEndDate.Value)
        {
            MessageBox.Show(
                this,
                "종료 날짜는 시작 날짜보다 늦어야 합니다.");

            return false;
        }

        if (nudInitialBalance.Value <= 0)
        {
            MessageBox.Show(
                this,
                "초기 투자금을 확인해주세요.");

            return false;
        }

        return true;
    }

    private bool CheckSelected()
    {
        if (_selectedCompetitionId <= 0)
        {
            MessageBox.Show(
                this,
                "대회를 먼저 선택해주세요.");

            return false;
        }

        return true;
    }

    private bool Confirm(string message)
    {
        return MessageBox.Show(
            this,
            message,
            "확인",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question)
            == DialogResult.Yes;
    }

    private void ClearInput()
    {
        _selectedCompetitionId = 0;
        _selectedApplicationId = 0;

        gridCompetitions.ClearSelection();
        gridCompetitions.CurrentCell = null;

        gridApplications.DataSource = null;

        lblApplicationStatus.Text =
            "대회를 선택하면 신청자 목록이 표시됩니다.";

        txtTitle.Clear();

        dtpStartDate.Value = DateTime.Now;
        dtpEndDate.Value = DateTime.Now.AddDays(7);

        nudInitialBalance.Value = 100000000;

        UpdateActionButtons();
        UpdateApplicationButtons();
    }

    private void UpdateActionButtons()
    {
        Competition? selected = _competitions.FirstOrDefault(
            x => x.CompetitionId == _selectedCompetitionId);

        bool isReady =
            selected?.Status == "준비";

        bool isRunning =
            selected?.Status == "진행중";

        btnUpdate.Enabled = isReady;
        btnDelete.Enabled = isReady;
        btnStart.Enabled = isReady;
        btnEnd.Enabled = isRunning;

        btnFinalRanking.Enabled =
            selected != null && selected.Status == "종료";

        btnCancel.Enabled = isReady || isRunning;
    }

    private void SetBusy(bool busy)
    {
        btnCreate.Enabled = !busy;
        btnRefresh.Enabled = !busy;
        btnClear.Enabled = !busy;

        gridCompetitions.Enabled = !busy;
        gridApplications.Enabled = !busy;

        if (busy)
        {
            btnUpdate.Enabled = false;
            btnDelete.Enabled = false;
            btnStart.Enabled = false;
            btnEnd.Enabled = false;
            btnFinalRanking.Enabled = false;
            btnCancel.Enabled = false;

            btnRefreshApplications.Enabled = false;
            btnApproveApplication.Enabled = false;
            btnRejectApplication.Enabled = false;
        }
        else
        {
            UpdateActionButtons();
            UpdateApplicationButtons();
        }
    }

    private void ShowError(Exception ex)
    {
        MessageBox.Show(
            this,
            ex.Message,
            "대회 관리 오류",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }
}

using System.IO.Compression;
using System.Text;
using MockInvestment.Api.Models;
using System.Text.RegularExpressions;

namespace MockInvestment.Api.Services;

public class StockMasterService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public StockMasterService(
        IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<List<string>> DownloadLinesAsync(
        string market,
        CancellationToken cancellationToken)
    {
        // 1. 시장에 따라 파일명 선택
        string fileName = market switch
        {
            "KOSPI" => "kospi_code.mst",
            "KOSDAQ" => "kosdaq_code.mst",
            _ => throw new ArgumentException(
                "시장은 KOSPI 또는 KOSDAQ이어야 합니다.",
                nameof(market))
        };

        string url =
            "https://new.real.download.dws.co.kr/"
            + $"common/master/{fileName}.zip";

        using var client =
            _httpClientFactory.CreateClient("StockMaster");

        // 2. 압축파일 다운로드
        using var response = await client.GetAsync(
            url,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        byte[] zipBytes = await response.Content
            .ReadAsByteArrayAsync(cancellationToken);

        // 3. 메모리에서 압축파일 열기
        using var zipStream = new MemoryStream(zipBytes);

        using var archive =
            new ZipArchive(zipStream, ZipArchiveMode.Read);

        var entry = archive.GetEntry(fileName)
            ?? throw new InvalidOperationException(
                $"압축파일에 {fileName}이 없습니다.");

        // 4. 한글을 읽기 위한 CP949 인코딩 설정
        Encoding.RegisterProvider(
            CodePagesEncodingProvider.Instance);

        Encoding encoding = Encoding.GetEncoding(
            949,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);

        using var entryStream = entry.Open();

        using var reader = new StreamReader(
            entryStream,
            encoding,
            detectEncodingFromByteOrderMarks: false);

        // 5. 원본 데이터를 한 줄씩 읽기
        var lines = new List<string>();

        while (await reader.ReadLineAsync(cancellationToken)
               is string line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                lines.Add(line);
            }
        }

        return lines;
    }

    public async Task<List<StockMasterItem>> GetStocksAsync(
    string market,
    CancellationToken cancellationToken)
    {
        int extraLength = market switch
        {
            "KOSPI" => 227,
            "KOSDAQ" => 221,
            _ => throw new ArgumentException(
                "지원하지 않는 시장입니다.",
                nameof(market))
        };

        var lines = await DownloadLinesAsync(
            market,
            cancellationToken);

        var stocks = new List<StockMasterItem>();
        var codes = new HashSet<string>();

        foreach (string line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int nameEnd = line.Length - extraLength;

            // 코드 21자리 뒤에 종목명이 있어야 함
            if (nameEnd <= 21)
            {
                throw new InvalidOperationException(
                    "종목정보 파일의 길이가 예상 구조와 다릅니다.");
            }

            string stockCode = line[..9].Trim();

            string stockName =
                line.Substring(21, nameEnd - 21).Trim();

            // 현재 서비스가 지원하는 숫자 6자리 코드만 선택
            if (!Regex.IsMatch(stockCode, @"^[0-9]{6}$"))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(stockName)
                || stockName.Length > 100)
            {
                throw new InvalidOperationException(
                    "종목명이 비어 있거나 허용 길이를 초과했습니다.");
            }

            if (!codes.Add(stockCode))
            {
                throw new InvalidOperationException(
                    $"중복 종목코드가 있습니다: {stockCode}");
            }

            stocks.Add(new StockMasterItem
            {
                StockCode = stockCode,
                StockName = stockName,
                Market = market
            });
        }

        if (stocks.Count == 0)
        {
            throw new InvalidOperationException(
                "추출된 종목이 없습니다.");
        }

        return stocks;
    }
}
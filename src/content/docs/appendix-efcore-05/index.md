---
title: "付録 EF Core 5：パフォーマンス"
description: "EF Core の計測と診断、インデックス設計、コンパイル済みクエリとモデル、NativeAOT、変更検出のコストの下げ方を解説します。第8章の付録です。"
---

このページは [第8章：データベースアクセスと ORM (Entity Framework Core)](../08-entity-framework-core/index.md) の付録です。計測と診断、クエリとモデルの最適化、実行時コストの削減を扱います。

本編を先に読んでから、必要な項目をここで参照してください。

**第8章のほかの付録**

- [付録 EF Core 1：モデル定義（エンティティとリレーションシップ）](../appendix-efcore-01/index.md)
- [付録 EF Core 2：モデル定義（キー・採番・SQL Server 固有）](../appendix-efcore-02/index.md)
- [付録 EF Core 3：マイグレーションの詳細とクエリ](../appendix-efcore-03/index.md)
- [付録 EF Core 4：更新・トランザクション・レプリカ](../appendix-efcore-04/index.md)
- [付録 EF Core 6：テスト](../appendix-efcore-06/index.md)


---

## 目次

1. [計測と診断](#1-計測と診断)
   - [まず計測する](#まず計測する)
   - [ログの出力形式を変える](#ログの出力形式を変える)
   - [EF Core が公開しているメトリックを見る](#ef-core-が公開しているメトリックを見る)
   - [クエリタグでログと LINQ を結びつける](#クエリタグでログと-linq-を結びつける)
   - [パラメーター名が EF Core 10 で変わった](#パラメーター名が-ef-core-10-で変わった)
   - [本番環境でコマンドログを出し続けない](#本番環境でコマンドログを出し続けない)
   - [ログとセキュリティ](#ログとセキュリティ)
   - [DbContext の構成と接続](#dbcontext-の構成と接続)
   - [Singleton やバックグラウンドサービスから DbContext を使う](#singleton-やバックグラウンドサービスから-dbcontext-を使う)
2. [クエリとモデルの最適化](#2-クエリとモデルの最適化)
   - [インデックスを正しく張る](#インデックスを正しく張る)
   - [実行プランはデータの量で変わる](#実行プランはデータの量で変わる)
   - [コレクションのパラメーター化と IN 句の翻訳](#コレクションのパラメーター化と-in-句の翻訳)
   - [コンパイル済みクエリ](#コンパイル済みクエリ)
   - [コンパイル済みモデル](#コンパイル済みモデル)
   - [NativeAOT と事前コンパイル済みクエリ](#nativeaot-と事前コンパイル済みクエリ)
3. [実行時のコストを下げる](#3-実行時のコストを下げる)
   - [DbContext プーリングでインスタンスを使い回す](#dbcontext-プーリングでインスタンスを使い回す)
   - [変更検出のコストを理解する](#変更検出のコストを理解する)
   - [バッファリングとストリーミング](#バッファリングとストリーミング)
   - [非同期 API を使う](#非同期-api-を使う)
   - [プロバイダーを替えるとモデルの意味が変わる](#プロバイダーを替えるとモデルの意味が変わる)
   - [Cosmos DB の接続と保存で注意すること](#cosmos-db-の接続と保存で注意すること)
   - [Cosmos DB の全文検索とベクトル検索](#cosmos-db-の全文検索とベクトル検索)
   - [同時実行検出を無効にしてはいけない](#同時実行検出を無効にしてはいけない)
4. [参考ドキュメント](#4-参考ドキュメント)

---

## 1. 計測と診断

> [!NOTE]
> **この付録に載せた性能比較の測定条件について。** 各節の数値は、その節に記載したモデル・データ量・接続先での個別の観測です。次の表は追加の追試で確認した環境の例で、過去のすべての測定が同一環境だったことを保証するものではありません。性能の優劣もハードウェア・データ・ネットワークによって変わるため、**各比較の条件とともに結果を読み、自分のアプリケーションで計測**してください。
>
> | 項目 | 値 |
> | --- | --- |
> | マシン | Apple M1 Max（10 コア）、macOS 26.6.2（追加追試時） |
> | .NET SDK | 10.0.400 |
> | EF Core | 10.0.11 |
> | SQLite | ローカルファイル（同一マシン） |
> | SQL Server | SQL Server 2022（Linux）。ローカルコンテナーと Azure 上のリモート接続は各節で区別 |
>
> 特に SQL Server 側の数値は**ネットワーク往復を含む**ため、同一ネットワーク内やローカル接続では大きく変わります。

### まず計測する

最適化の前に、どのクエリが遅いのかを特定します。EF Core は実行した SQL をログに出力できます。

```csharp
builder.Services.AddDbContext<BloggingContext>(options =>
    options.UseSqlServer(connectionString)
           .LogTo(Console.WriteLine, LogLevel.Information));
```

> [!TIP]
> ASP.NET Core では、この `LogTo` を書かなくてもログは出力されます。公式ドキュメントは「`AddDbContext` または `AddDbContextPool` を呼び出すと、EF Core は通常の ASP.NET のしくみで構成されたログ設定を自動的に使う」と説明しています。つまり `appsettings.json` の `Logging` セクションで `Microsoft.EntityFrameworkCore.Database.Command` のレベルを `Information` にすれば、実行された SQL が既定のロガーに流れます。
>
> ```json
> {
>   "Logging": {
>     "LogLevel": {
>       "Microsoft.EntityFrameworkCore.Database.Command": "Information"
>     }
>   }
> }
> ```
>
> `LogTo` は、コンソールアプリケーションのように DI を使わない場合や、一時的に手元で SQL を眺めたい場合に便利な**簡易ログ**の手段です。

開発環境では、しきい値を超えたコマンドだけを警告として記録すると、遅いクエリを見つけやすくなります。実運用では Application Insights などの APM (Application Performance Monitoring) ツールでデータベース依存関係を追跡します。

`ToQueryString()` を使うと、クエリを実行せずに生成される SQL を確認できます。SQL Server 向けのこの例では先頭にパラメーターの `DECLARE` が付きます。ただし公式 API はデバッグ用途としており、任意の出力をそのまま実行できる保証はありません。SQL Server Management Studio で試す場合も、構文・パラメーター・対象データベースを確認してください。

```csharp
var url = "dotnet";
var query = context.Blogs.Where(b => b.Url.Contains(url));
Console.WriteLine(query.ToQueryString());
```

SQL 文字列ではなく `DbCommand` そのものが欲しい場合は `CreateDbCommand()` を使います。`CommandText` に加えて、パラメーターの値・型・サイズ、コマンドのタイムアウトまで取得できます。

```csharp
using System.Data.Common;

using var command = query.CreateDbCommand();

Console.WriteLine(command.CommandText);
Console.WriteLine(command.CommandTimeout);
foreach (DbParameter p in command.Parameters)
{
    Console.WriteLine($"{p.ParameterName} = {p.Value} ({p.DbType}, size={p.Size})");
}
```

EF Core 10.0.11 の SQL Server プロバイダーで、データベース接続を開かずにコマンドを生成した結果は次のとおりです。`ToQueryString()` と違って `CommandText` にはパラメーターの値が埋め込まれず、`Parameters` コレクションから個別に取得します。

```text
SELECT [b].[Id], [b].[Name], [b].[Url]
FROM [Blogs] AS [b]
WHERE [b].[Url] LIKE @url_contains ESCAPE N'\'

CommandTimeout : 30
@url_contains = %dotnet% (String, size=4000)
```

> [!WARNING]
> 公式 API リファレンスは `CreateDbCommand()` について 2 つの注意を挙げています。1 つは、**返される `DbCommand` は `IDisposable` であり、破棄の責任は呼び出し側にある**こと。もう 1 つは、**このコマンドを直接実行しても EF Core が実行した場合と同じ動作になる保証はない**ことです。診断目的に限って使ってください。

### ログの出力形式を変える

`LogTo` は既定で複数行にわたる読みやすい形式で出力しますが、ログ基盤に流し込むときは 1 行にまとめたい、時刻を UTC で揃えたい、といった要求が出てきます。`DbContextLoggerOptions` で制御できます。

```csharp
options.UseSqlServer(connectionString)
       .LogTo(Console.WriteLine,
              new[] { RelationalEventId.CommandExecuted },
              LogLevel.Information,
              DbContextLoggerOptions.UtcTime | DbContextLoggerOptions.SingleLine);
```

次は SQLite で同じ `CountAsync()` クエリを使い、書式オプションの有無を比較した確認例です（EF Core 10.0.11）。

```text
--- 書式オプション未指定（既定） ---
info: 2026/09/10 09:55:23.500 RelationalEventId.CommandExecuted[20101] (Microsoft.EntityFrameworkCore.Database.Command)
      Executed DbCommand (0ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      SELECT COUNT(*)
      FROM "Blogs" AS "b"

--- UtcTime | SingleLine ---
2026-09-10T00:55:23.5456160Z -> Executed DbCommand (0ms) [Parameters=[], CommandType='Text', CommandTimeout='30']SELECT COUNT(*)FROM "Blogs" AS "b"
```

`UtcTime` を付けると ISO 8601 の UTC タイムスタンプが先頭に付き、`SingleLine` を付けると改行が取り除かれて 1 行になります。

> [!WARNING]
> [公式のログ書式の説明](https://learn.microsoft.com/ja-jp/ef/core/logging-events-diagnostics/simple-logging#single-line-logging)では、`SingleLine` はメッセージを 1 行で出力する設定です。この確認例では `...CommandTimeout='30']SELECT` のように区切りの空白がありません。特定の空白や改行の置換規則を一般契約とせず、出力先で形式を扱えることを確認してください。

> [!TIP]
> `LogTo` の書式オプションを省略した場合、既定は `DefaultWithLocalTime` で、現在のカルチャーに従ったローカル時刻が出力されます。[公式のログ書式の説明](https://learn.microsoft.com/ja-jp/ef/core/logging-events-diagnostics/simple-logging#message-contents-and-formatting)も同じです。ほかのメタデータを維持して UTC 時刻へ変えるには `DefaultWithUtcTime` を指定します。

### EF Core が公開しているメトリックを見る

[公式のメトリックガイド](https://learn.microsoft.com/ja-jp/ef/core/logging-events-diagnostics/metrics)では、EF Core 9 以降に `System.Diagnostics.Metrics` API で **`Microsoft.EntityFrameworkCore`** というメーターを公開すると説明しています。アプリケーション全体の傾向を把握するための 7 つの計測器は次のとおりです。

| メトリック | 種類 | 意味 |
| --- | --- | --- |
| `microsoft.entityframeworkcore.active_dbcontexts` | ObservableUpDownCounter | 現在アクティブな `DbContext` の数 |
| `microsoft.entityframeworkcore.queries` | ObservableCounter | 実行されたクエリの累計 |
| `microsoft.entityframeworkcore.savechanges` | ObservableCounter | `SaveChanges` の累計 |
| `microsoft.entityframeworkcore.compiled_query_cache_hits` | ObservableCounter | クエリキャッシュにヒットした回数 |
| `microsoft.entityframeworkcore.compiled_query_cache_misses` | ObservableCounter | クエリキャッシュを外した回数 |
| `microsoft.entityframeworkcore.execution_strategy_operation_failures` | ObservableCounter | 実行戦略が捉えた操作の失敗回数 |
| `microsoft.entityframeworkcore.optimistic_concurrency_failures` | ObservableCounter | 楽観的同時実行制御の失敗回数 |

次は `MeterListener` による確認例です。**同じ形のクエリを 3 回、別の形を 1 回、`SaveChanges` を 1 回**実行した条件の結果を示します。

```text
microsoft.entityframeworkcore.active_dbcontexts = 1
microsoft.entityframeworkcore.queries = 4
microsoft.entityframeworkcore.savechanges = 1
microsoft.entityframeworkcore.compiled_query_cache_hits = 2
microsoft.entityframeworkcore.compiled_query_cache_misses = 2
microsoft.entityframeworkcore.execution_strategy_operation_failures = 0
microsoft.entityframeworkcore.optimistic_concurrency_failures = 0
```

`active_dbcontexts` が 1 なのは、`DbContext` を破棄する前に観測したためです。破棄した後に観測すると 0 になります。`misses` が 2 なのは、**クエリの形が 2 種類**あったからです。同じ形の 2 回目と 3 回目は `hits` に入っています。`misses` の増分を同期間の `hits` の増分と比較すると、観測したクエリキャッシュの利用状況を調べられます。動的に組み立てた式ツリーや、定数を埋め込んでしまったクエリが原因になりがちです。

```csharp
using MeterListener meterListener = new();
meterListener.InstrumentPublished = (instrument, listener) =>
{
    if (instrument.Meter.Name == "Microsoft.EntityFrameworkCore")
    {
        listener.EnableMeasurementEvents(instrument);
    }
};
meterListener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
    Console.WriteLine($"{instrument.Name} = {value}"));
meterListener.SetMeasurementEventCallback<int>((instrument, value, _, _) =>
    Console.WriteLine($"{instrument.Name} = {value}"));
meterListener.Start();

// ここでクエリを実行する

meterListener.RecordObservableInstruments();
```

> [!WARNING]
> **落とし穴が 2 つあります。どちらも「メトリックが取れない」という同じ症状になります。**
>
> 1 つ目は、上のメトリックが**観測可能な計測器 (observable instrument)** であることです。[公式の `RecordObservableInstruments` API](https://learn.microsoft.com/ja-jp/dotnet/api/system.diagnostics.metrics.meterlistener.recordobservableinstruments?view=net-10.0)で値を取得します。上のように `MeterListener` を直接使う確認例でも、同呼び出しなしではコールバックの通知はありません。
>
> 2 つ目は、[EF Core 10.0.11 の公式実装](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Infrastructure/Internal/EntityFrameworkMetrics.cs)で、`active_dbcontexts` が `ObservableUpDownCounter<int>`、残りが `ObservableCounter<long>` であることです。この版の確認例でも `<long>` のコールバックだけでは前者を取得できず、上のコードは `<int>` も登録しています。

> [!TIP]
> `compiled_query_cache_misses` と `optimistic_concurrency_failures` は累積カウンターです。前者の増分は同期間のヒット数や実行状況と比較し、後者の増分は観測時間で割るなどして競合の発生頻度を調べます。累積値だけをヒット率や発生頻度と読み替えず、アプリケーションの通常時の値に合わせてアラート条件を決めてください。

### クエリタグでログと LINQ を結びつける

ログに大量の SQL が流れると、「この重いクエリはソースコードのどこが出しているのか」が分からなくなります。**クエリタグ (Query Tag)** を使うと、LINQ クエリに付けた注釈が SQL のコメントとしてそのまま出力されます。

```csharp
var blogs = await context.Blogs
    .TagWith("月次レポート用の集計")
    .ToListAsync();
```

生成される SQL は次のようになります（EF Core 10.0.11 / SQLite、`Id` と `Name` を持つ最小モデルで確認）。この出力例を全プロバイダーに共通の書式とはみなさないでください。

```sql
-- 月次レポート用の集計

SELECT "b"."Id", "b"."Name"
FROM "Blogs" AS "b"
```

タグは**累積します**。共通のクエリを組み立てるヘルパーメソッドの中で `TagWith` を呼び、呼び出し側でさらに `TagWith` を呼べば、両方がコメントとして残ります。加えて `TagWithCallSite()` を使うと、**そのクエリを書いたファイル名と行番号**が自動で埋め込まれます。

```csharp
var blogs = await context.Blogs
    .TagWith("1つ目")
    .TagWith("2つ目")
    .TagWithCallSite()
    .ToListAsync();
```

こちらも同じ最小モデルで SQL 生成と実行を確認した例です。`TagWithCallSite()` のファイルパスと行番号は呼び出し元によって変わります。

```sql
-- 1つ目
-- 2つ目
-- File: /path/to/Program.cs:6

SELECT "b"."Id", "b"."Name"
FROM "Blogs" AS "b"
```

SQL Server のクエリストアや実行計画の分析ツールでは、このコメントごとキャプチャされるため、データベース側で見つけた重いクエリからアプリケーションのコードへ一発でたどり着けます。

> [!NOTE]
> EF Core のクエリタグは `TagWith` を呼ぶと使えます。`TagWithCallSite()` では呼び出し位置を自動的に埋め込めます。

> [!TIP]
> パフォーマンスを調べるときは、EF Core の細かな設定より先に「不要な列を取りすぎていないか」「N+1 が起きていないか」「必要なインデックスがあるか」を確認します。ここまでで説明した `AsNoTracking`、投影、`Include`、`AsSplitQuery`、ページングを先に見直してください。`AsSplitQuery` の判断基準は[付録3の「単一クエリと分割クエリ」](../appendix-efcore-03/index.md#単一クエリと分割クエリ)にあります。

#### メトリックで全体像をつかむ

個々のクエリを見る前に、アプリケーション全体の傾向を数値で押さえます。EF Core は `Microsoft.EntityFrameworkCore` という名前の [`Meter`](https://learn.microsoft.com/ja-jp/dotnet/core/diagnostics/metrics)（.NET の計測 API の単位。詳しくは「メトリックの概要」を参照）を通じて次のカウンターを公開しています（EF Core 10.0.11 で実際に列挙して確認）。

| メトリック名 | 意味 |
| --- | --- |
| `microsoft.entityframeworkcore.active_dbcontexts` | 生存している `DbContext` の数 |
| `microsoft.entityframeworkcore.queries` | 実行されたクエリの累計 |
| `microsoft.entityframeworkcore.savechanges` | `SaveChanges` の累計 |
| `microsoft.entityframeworkcore.compiled_query_cache_hits` | クエリキャッシュにヒットした回数 |
| `microsoft.entityframeworkcore.compiled_query_cache_misses` | クエリキャッシュを外した回数 |
| `microsoft.entityframeworkcore.execution_strategy_operation_failures` | 再試行戦略が捉えた失敗の回数 |
| `microsoft.entityframeworkcore.optimistic_concurrency_failures` | 楽観的同時実行制御の競合回数 |

特に重要なのが **クエリキャッシュのヒット率** です。EF Core は LINQ 式から SQL への変換結果をキャッシュしており、起動直後を過ぎればヒット率はほぼ 100% になるはずです。`compiled_query_cache_misses` は累積値なので、増え続けることだけでは効率を判断できません。同期間の `hits` と `misses` の増分から比率を調べ、新しい形のクエリが増えていないか確認します。

次は、同じ処理を 50 回ずつ繰り返した条件でのヒット率の確認例です。

| 書き方 | ヒット率 |
| --- | --- |
| 同じ形の LINQ クエリを繰り返す | 98% |
| `EF.Constant()` で値をインライン化する | 98% |
| 条件式を実行時に付け外しして形を変える | 92% |
| `FromSqlRaw` に、値を直接埋め込んだ毎回異なる SQL を渡す | **0%** |

> [!NOTE]
> [EF Core 9 の破壊的変更](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/breaking-changes#efconstant-and-efparameter-no-longer-work-inside-compiled-queries)では、`EF.Constant()` の処理をクエリキャッシュより後の段階へ移し、定数値ごとの再コンパイルを避けると説明しています。この表でも同じ式の形で値だけを変える条件は、50 回中 49 ヒット・1 ミスです。式の形が変わる場合までヒット率を保証するものではなく、データベース側のプランキャッシュへの影響は別に確認します。
>
> ただしこれは EF Core 9 以降の挙動です。EF Core 8 の実装では `EF.Constant()` がクエリキャッシュより前の段階で定数ノードを埋め込んでいたため、値が変わるたびに EF Core 側でもキャッシュミスが発生していました。EF Core 9 でこの処理はパイプラインの後段へ移されています。
>
> [EF Core 10.0.11 の公式実装](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore.Relational/Query/Internal/FromSqlQueryRootExpression.cs)は、生 SQL のクエリ式の等価比較に SQL 文字列を含めます。同版と SQLite で 50 回ずつ比較した結果は、同じ SQL の繰り返しとパラメーター化では各 1 回、毎回異なる SQL では 50 回のコンパイルです。**文字列連結を使うこと自体がヒット率 0% の条件ではありません。**
>
> 同期間のヒット・ミスから求めた比率が低いなら、まず生 SQL の組み立て方と、条件を動的に付け外ししている箇所を疑ってください。パラメーター化を保ったまま生の SQL を書く方法は[付録3の「生の SQL を使う」](../appendix-efcore-03/index.md#生の-sql-を使う)を参照してください。

`active_dbcontexts` が想定より多いままなら `DbContext` が破棄されずに残っている可能性があり、`optimistic_concurrency_failures` の増加は同時更新の競合が実際に起きていることを示します。これらは OpenTelemetry や Application Insights にそのまま送れます。

> [!TIP]
> **`active_dbcontexts` は「正常な状態」を先に知っておくと役に立ちます。** 次は、`AddDbContextPool` を使い、スコープごとにクエリを 1 回発行する処理を 10 分間で 2,800 回繰り返した条件での記録です。
>
> | 経過 | 反復回数 | マネージドヒープ | ワーキングセット | `active_dbcontexts` |
> | --- | --- | --- | --- | --- |
> | 42 秒 | 200 | 8.6 MiB | 139.4 MiB | 1 |
> | 292 秒 | 1,400 | 9.6 MiB | 141.1 MiB | 1 |
> | 600 秒 | 2,800 | 8.9 MiB | 141.2 MiB | 1 |
>
> この観測では、マネージドヒープは 4 MiB 台まで戻る変動があり、42 秒から 600 秒までのワーキングセット増加は約 1.8 MiB です（1 MiB = 1,048,576 バイト）。`active_dbcontexts` は**各記録時点で 1**ですが、この値だけで全インスタンスの正しい返却まで証明できるわけではありません。
>
> この値が反復回数に比例して増えていく場合は、`DbContext` を `using` や DI スコープの外で作って破棄し忘れている可能性があります。負荷をかけた状態でこのメトリックが横ばいになるかどうかを、リリース前に一度確認しておくとよいでしょう。

### パラメーター名が EF Core 10 で変わった

ログや実行プランに現れる SQL パラメーターの名前は、EF Core 10 で**変数名そのもの**に変わりました。

```csharp
var city = "London";
var blogs = await context.Blogs.Where(b => b.City == city).ToListAsync(cancellationToken);
```

SQL Server で実測した SQL は次のとおりです。

```sql
-- EF Core 9 まで
@__city_0='London'
WHERE [b].[City] = @__city_0

-- EF Core 10 以降（実測）
@city='London'
WHERE [b].[City] = @city
```

名前が重複する場合にだけ数字が付きます。読みやすさの改善であり、ほとんどのアプリケーションには影響しませんが、次の 2 つは実害が出ます。

- **SQL の文字列を比較するテスト。** `ToQueryString()` の結果や、インターセプターで受け取った `DbCommand.CommandText` を期待値と突き合わせているコードのうち、変更されたパラメーター名を期待値に含めるものは更新が必要です。`DbParameter.ParameterName` を直接見ているコードも同様です（インターセプターの実装は[付録4の「インターセプターによる横断的な処理」](../appendix-efcore-04/index.md#インターセプターによる横断的な処理)を参照）。
- **クエリプランのキャッシュ。** パラメーター名は SQL 文字列の一部なので、変更後の SQL に対してプランの再コンパイルが発生する可能性があります。公式も「大規模なシステムでは、配置直後に一時的なコンパイルのスパイクが起きることを見込んでおくべき」と述べています。負荷の高い時間帯を避けて配置してください。

### 本番環境でコマンドログを出し続けない

公式ドキュメントは「**本番環境でコマンドの実行ログを有効にしたままにするのは、たいてい悪い考えだ**」と述べています。理由は 2 つあり、**ログの出力自体がアプリケーションを遅くすること**と、**巨大なログファイルが短時間でサーバーのディスクを埋めること**です。公式は、データを集めるなら「アプリケーションを注意深く監視しながら短時間だけ有効にする」か「本番前の環境で採取する」ことを勧めています。

以下は公式の注意点に対する条件付きの補足です。まず、SQL Server 2022 にウォームアップ 1 回と主キー検索 2,000 回を実行した場合の、`LogTo` のファイル出力量を示します。

| 項目 | 実測値 |
| --- | --- |
| ウォームアップ 1 回と主キー検索 2,000 回分のログ | 約 635 KiB（1 KiB = 1,024 バイト） |
| 1 クエリあたり | 約 330 バイト |

この目安を 1 クエリあたり 330 バイトとして、1 秒あたり 100 クエリを 24 時間処理すると、ログ量は **約 2.9 GB** になります（330 × 100 × 86,400 = 2,851,200,000 バイト、1 GB = 10 億バイトで計算）。これは処理量からの試算であり、すべてのクエリが同じログ量になるという保証ではありません。ログの保存先を分けず、ローテーションも設定していないサーバーでは、これだけでディスクが埋まります。

次に実行時間です。出力先とデータベースが異なる 2 条件を示します。これらの差をデータベースの応答速度だけに帰属させる比較ではありません。

| 環境 | ログなし | ログあり | 差 |
| --- | --- | --- | --- |
| SQL Server 2022（コンテナー、2,000 クエリ） | 1,431 ms | 1,392 ms | 差を検出できず |
| SQLite インメモリ（5,000 クエリ） | 243 ms | 324 ms | 約 1.33 倍遅い |

ログなしの総時間をクエリ数で割ると、SQL Server は約 0.7 ミリ秒、SQLite は約 0.05 ミリ秒です。この SQLite の測定点ではログありの時間が約 33% 長いものの、SQL Server 側との差の理由や統計的な有意差は、この結果だけでは判断できません。

> [!NOTE]
> この 2 条件の時間差を、データベースの速さだけに帰属させることはできません。SQL Server 側は `StreamWriter`（計時後に `Flush`）、SQLite 側はメモリ上の `StringWriter` への出力で、後者はディスク書き込みを含みません。ファイルの容量・書き込み時間も実際の出力先で確認し、本番の詳細ログは必要な期間に限定します。

---

### ログとセキュリティ

EF Core は、既定ではパラメーター値をログに出力しません。パラメーター名やサイズなどのメタデータは残りますが、値は `?` に置き換えられます。次は SQL Server 2022 に対し、`city` というローカル変数で絞り込む確認例です。

```text
[Parameters=[@city='?' (Size = 4000)], CommandType='Text', CommandTimeout='30']
SELECT [b].[Id], [b].[City], [b].[Name]
FROM [Blogs] AS [b]
WHERE [b].[City] = @city
```

> [!NOTE]
> **EF Core 10 でパラメーター名の付け方が変わりました。** EF Core 9 までは `__` プレフィックスと連番を付けた `@__city_0` のような名前でしたが、EF Core 10 では **元になった変数やメンバーの名前がそのまま使われ**、重複するときだけ末尾に数字が付きます。生成される SQL が読みやすくなり、ログやクエリプランをコードと対応付けやすくなったという理由です。
>
> ほとんどのアプリケーションではこの変更を意識する必要はありませんが、**生成された SQL の文字列を比較するスナップショットテストや、`DbCommand.CommandText` を解析するインターセプター・ロガーのうち、変更された名前に依存するものは修正が必要**です。
>
> なお、この簡素化は LINQ クエリのパラメーターについての変更です。この確認例の `SaveChanges` による `INSERT` / `UPDATE` では、`@p0`、`@p1` を確認できています。

ただし EF Core は、状況によっては値を SQL に **インライン化** します。`EF.Constant()` が代表例です。[EF Core 10 の新機能](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-10.0/whatsnew)では、このような本来パラメーターになる値のログ上のリダクションを説明しています。次は SQL Server 形式の模式例です。直接リテラル・`EF.Constant()`・通常のパラメーターの比較は EF Core 10.0.11 / SQLite で確認できています。

```text
-- EF.Constant(name) を使ったクエリ
-- EF Core 9 まで
... WHERE [b].[Name] = N'Contoso'

-- EF Core 10 以降（既定）
... WHERE [b].[Name] = ?
```

> [!WARNING]
> このリダクションの対象は **本来パラメーターになるはずの値がインライン化されたもの** に限られます。`Where(b => b.Name == "Contoso")` のように LINQ 式へ直接書いたリテラルは、クエリごとに変化しない真の定数として扱われるため **リダクションされず、ログにそのまま出力されます**（EF Core 10.0.11 で実測）。ログに出したくない値をクエリへ直接埋め込まないでください。

デバッグのために実際のパラメーター値を見たい場合は `EnableSensitiveDataLogging()` を有効にします。

```csharp
builder.Services.AddDbContext<BloggingContext>(options =>
{
    options.UseSqlServer(connectionString);

    if (builder.Environment.IsDevelopment())
    {
        options.EnableSensitiveDataLogging();
        options.EnableDetailedErrors();
    }
});
```

> [!WARNING]
> `EnableSensitiveDataLogging` は、パラメーター値やエンティティのキー値をログに出力します。個人情報や資格情報が漏えいする可能性があるため、本番環境では絶対に有効にしないでください。上の例のように、開発環境に限定して有効化します。

---

### DbContext の構成と接続

#### EF Core 10 は接続文字列に Application Name を追加する

[EF Core 10 の破壊的変更](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-10.0/breaking-changes#application-name-is-now-injected-into-the-connection-string)では、未指定の `Application Name` の自動追加と、EF 以外のアクセスとの接続プールの分離・分散トランザクションへの昇格の可能性を説明しています。次は SQL Server プロバイダーでの接続文字列の確認例です。

```text
[渡した接続文字列]
Server=localhost;Database=Test;Trusted_Connection=True;TrustServerCertificate=True

[EF Core が使う文字列]
Data Source=localhost;Initial Catalog=Test;Integrated Security=True;
Trust Server Certificate=True;Application Name="EFCore/10.0.11 (macOS 26.6.2 Arm64)"
```

ほとんどの場合は影響しませんが、**同じデータベースに EF Core と Dapper や ADO.NET などを併用している場合は注意が必要です。** SqlClient は接続文字列が異なると別の接続プールを使うため、両者が別々のプールに分かれます。同一の `TransactionScope` 内で別プールの接続を使うと、**分散トランザクションへの昇格** が必要になる可能性があります。以下は接続を 2 本開いた条件での観測です。

> [!WARNING]
> **暗黙の分散トランザクションへの昇格は既定で無効です。** [公式 API](https://learn.microsoft.com/ja-jp/dotnet/api/system.transactions.transactionmanager.implicitdistributedtransactions?view=net-10.0)は既定値 `false` と Windows 限定の設定であることを示しています。Windows Server 2022 + SQL Server 2022 + .NET 10 の確認例では、異なる接続文字列で 2 本開くと、2 本目の `Open()` で次の例外を確認できています。
>
> ```text
> System.NotSupportedException: Implicit distributed transactions have not been enabled.
> If you're intentionally starting a distributed transaction, set
> TransactionManager.ImplicitDistributedTransactions to true.
> ```
>
> 同じ Windows 環境で `TransactionManager.ImplicitDistributedTransactions = true` を設定した確認例では、2 本目の `Open()` 後に `DistributedIdentifier` が `Guid.Empty` 以外となり、MSDTC への昇格を確認できています。macOS / .NET 10 の確認例は、同プロパティの設定時に `PlatformNotSupportedException` です。

次は同じ環境で接続文字列と開閉順序を変えた確認例です。表の結果は、あらゆる接続プールの状態で同じ動作を保証するものではありません。

| `TransactionScope` 内での操作 | 昇格するか |
| --- | --- |
| 同じ接続文字列の接続を 2 本 **同時に** 開く | 昇格する |
| 同じ接続文字列で、1 本目を閉じてから 2 本目を開く | **昇格しない** |
| `Application Name` だけが違う接続を 2 本同時に開く | 昇格する |
| `Application Name` だけが違う接続を、1 本目を閉じてから 2 本目を開く | **昇格する** |

[公式の破壊的変更](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-10.0/breaking-changes#application-name-is-now-injected-into-the-connection-string)が警告するのは、異なる接続文字列により接続プールが分かれ、以前は不要だった分散トランザクションへの昇格が必要になる**可能性**です。接続を順に閉じれば昇格しない、という一般契約を表の結果から導くことはできません。

公式の回避策は `Application Name` の明示です。[EF Core 10.0.11 の実装](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore.SqlServer/Storage/Internal/SqlServerConnection.cs)では空文字列やドライバーの既定名も置換対象なので、アプリケーション固有の値を指定します。SQL Server 2022 の `program_name` の確認例では、未指定は `EFCore/10.0.11 (macOS 26.6.2 Arm64)`、明示指定は `BloggingApi` です。置換の細部はこの版の実装に対する補足です。

```json
{
  "ConnectionStrings": {
    "BloggingDatabase": "Server=...;Database=Blogging;Trusted_Connection=True;Application Name=BloggingApi"
  }
}
```

#### AddDbContext の競合する設定は後の値が優先される

`AddDbContext` を同じ `DbContext` 型に対して複数回呼び出したとき、**どちらの構成が使われるか**は EF Core 8 で変わりました。競合する設定については、以前は最初の呼び出しが優先されましたが、現在は**最後の呼び出しが優先されます**。設定全体が置き換わるわけではなく、競合しない設定は組み合わせて適用されます。

```csharp
services.AddDbContext<BloggingContext>(o => o.UseSqlServer(first));
services.AddDbContext<BloggingContext>(o => o.UseSqlServer(second));
// → second が使われる
```

[公式の構成の優先順位](https://learn.microsoft.com/ja-jp/ef/core/dbcontext-configuration/#configuredbcontext-and-adddbcontext-precedence)では、登録順に構成を適用し、競合する項目だけ後の値を優先します。EF Core 10.0.11 / SQLite の確認例でも、接続先は後の値、最初の `NoTracking` は維持です。[この版の実装](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Extensions/EntityFrameworkServiceCollectionExtensions.cs#L597)は、`TryAdd` によるコンテキスト本体の登録と、構成処理の追加を分けています。本体の DI 登録とオプションの合成を混同しないでください。

> [!WARNING]
> ライブラリが内部で `AddDbContext` を呼んでいる場合は、同じ設定項目をどちらが上書きするか、登録順を確認してください。ただし、**後から登録すれば以前のプロバイダーも外れる、という意味ではありません。** 異なるプロバイダーへの切り替えは、[付録6の構成の除去](../appendix-efcore-06/index.md#webapplicationfactory-で-api-ごとテストする)も確認してください。

#### 複数の DbContext を登録するときは DbContextOptions<TContext> を受け取る

`DbContext` のコンストラクターは、非ジェネリックの `DbContextOptions` も受け取れます。1 つしか `DbContext` を登録していないうちは問題なく動くため、そのまま書いてしまいがちです。しかし公式ドキュメントは、**ジェネリックの `DbContextOptions<TContext>` を使うこと**を求めています。複数の `DbContext` を登録したときに、その型に対応する正しいオプションが DI から解決されるようにするためです。

```csharp
// 推奨
public class GoodContext(DbContextOptions<GoodContext> options) : DbContext(options);

// 複数登録すると壊れる
public class BadContext(DbContextOptions options) : DbContext(options);
```

次は 2 つの `DbContext` を登録する確認例です。非ジェネリックのオプションを受け取る側では、登録順による結果の違いを確認できています。

| 登録順 | `GoodContext` の解決 | `BadContext` の解決 |
| --- | --- | --- |
| `GoodContext` → `BadContext` | 成功 | **たまたま成功** |
| `BadContext` → `GoodContext` | 成功 | **失敗** |

この DI 構成では、非ジェネリック版は最後に登録された `DbContextOptions` を解決します。失敗側の確認結果は次の例外です。特定の登録順で成功しても、非ジェネリック版を使う根拠にはしないでください。

```text
System.InvalidOperationException: The DbContextOptions passed to the BadContext constructor
must be a DbContextOptions<BadContext>. When registering multiple DbContext types, make sure
that the constructor for each context type has a DbContextOptions<TContext> parameter rather
than a non-generic DbContextOptions parameter.
```

「今は 1 つしかないから」と非ジェネリック版で書くと、2 つ目の `DbContext` を追加した日に、しかも登録順によっては別の開発者の環境でだけ壊れます。最初からジェネリック版で書いてください。

> [!NOTE]
> 例外は、その `DbContext` 型自体を継承させたい場合です。公式ドキュメントは、基底クラスには非ジェネリックの `DbContextOptions` を受け取る `protected` コンストラクターを公開し、派生クラス側でジェネリック版を受け取るよう案内しています。継承を想定しないのであれば、クラスを `sealed` にしておくのが安全です。

#### 構成を後から足す（ConfigureDbContext）

`AddDbContext` はコンテキスト自体を DI に登録し、同時にプロバイダーや接続文字列などのオプションも構成する呼び出しです。これに対して、**コンテキストの登録とは分けてログや診断だけを足したい**ことがあります。テストで `EnableSensitiveDataLogging` を付けたい、共通ライブラリでインターセプターを差したい、といった場面です。

その用途には、EF Core 9 以降の **`ConfigureDbContext`** を使えます。[公式リファレンス](https://learn.microsoft.com/ja-jp/dotnet/api/microsoft.extensions.dependencyinjection.entityframeworkservicecollectionextensions.configuredbcontext?view=efcore-10.0)は、構成が呼び出し順に適用され、競合する設定は後の構成で上書きされると説明しています。**`AddDbContext` の再呼び出しも、以前の構成全体を消す操作ではありません。** EF Core 10 / SQLite の確認例でも、最初の接続設定を維持したまま、2 回目で追加したログ設定の有効化を確認できています。

`ConfigureDbContext` 自体はコンテキストを DI に登録しないため、`AddDbContext` などとの併用が必要です。同 API だけでは解決できないことも確認できています。次は、共通側の診断設定とアプリケーション側の登録を分けた例です。

```csharp
// ライブラリやテスト側：診断の設定だけを足す
services.ConfigureDbContext<BloggingContext>(options =>
    options.LogTo(Console.WriteLine)
           .EnableSensitiveDataLogging());

// アプリケーション側：プロバイダーと接続文字列を決める
services.AddDbContext<BloggingContext>(options =>
    options.UseSqlServer(connectionString));
```

この順番で登録した確認例では、プロバイダーは SQL Server のまま、`LogTo` と `EnableSensitiveDataLogging` の有効化を確認できています。

```text
プロバイダー: Microsoft.EntityFrameworkCore.SqlServer
Executed DbCommand (36ms) [Parameters=[@name='test' (Size = 4000)], ...]
```

`@name='test'` と実際の値が出力されているのが `EnableSensitiveDataLogging` の効果です。無効なら `@name='?'` と伏せられます。

公式ドキュメントによれば、`ConfigureDbContext` と `AddDbContext` は**呼び出した順に適用され、競合する設定は後の呼び出しが勝ちます**。競合しない設定（ログ、インターセプターなど）はすべて合成されます。上の例のように設定項目が競合しない場合は、`ConfigureDbContext` を `AddDbContext` の前後どちらに置いても構いません。

> [!NOTE]
> 公式ドキュメントは `ConfigureDbContext` を「再利用可能なライブラリやコンポーネント向けの中級者向け機能」と位置づけています。アプリケーション本体では通常の `AddDbContext` で十分です。
>
> また、**`ConfigureDbContext` で別のプロバイダーを構成しても、前のプロバイダーの構成は消えません。** プロバイダーを完全に差し替えたい場合は、登録そのものを削除して追加し直す必要があります。

#### サーバー側 Blazor での DbContext

サーバー側の Blazor は、リクエストごとではなく**ユーザーの接続（サーキット）単位で状態を保持する**アプリケーションフレームワークです。そのため Scoped の `DbContext` は、そのサーキット内の**複数のコンポーネントで共有**されます。`DbContext` はスレッドセーフではなく同時利用を想定していないため、公式ドキュメントは既存のライフタイムがいずれも適さないと説明しています。

| ライフタイム | 公式が挙げる問題 |
| --- | --- |
| Singleton | アプリケーションの全ユーザーで状態が共有され、不適切な同時利用になる |
| Scoped（既定） | 同じユーザーのコンポーネント間で同様の問題が起きる |
| Transient | 要求ごとに新しいインスタンスになるが、コンポーネントが長寿命になり得るため、意図より長寿命なコンテキストになる |

[公式のスレッドの問題の回避](https://learn.microsoft.com/ja-jp/ef/core/dbcontext-configuration/#avoiding-dbcontext-threading-issues)では、同じ `DbContext` の並行操作を禁止し、未検出でも安全ではないとしています。次は同じインスタンスの操作を重ねた確認例の例外です。常にこの型で検出されるという保証ではありません。

```text
System.InvalidOperationException: A second operation was started on this context
instance before a previous operation completed. This is usually caused by
different threads concurrently using the same instance of DbContext.
```

操作ごとに別の `IDbContextFactory<T>` 由来のインスタンスを使う確認例では、同じ 2 操作の成功を確認できています。これはデータベース上の競合を含むあらゆる例外を防ぐという意味ではありません。

公式ドキュメントが示す指針は次のとおりです。

- **操作ごとに 1 つのコンテキスト**を使うことを検討する（`DbContext` は生成コストが小さくなるよう設計されている）
- 同時実行を防ぐ**フラグ**（`Loading` など）を用意する。これはデータベース行のロックが目的ではなく、取得中に UI 操作をさせないためのもの
- 同じコード部分に複数のスレッドが入る可能性があるなら、**ファクトリを注入して操作ごとに新しいインスタンスを作る**
- 変更追跡や同時実行制御を活かす長めの操作では、**コンテキストをコンポーネントの寿命に合わせる**

> [!NOTE]
> ここで扱っているのは**サーバー側の Blazor** です。Blazor WebAssembly は WebAssembly のサンドボックス内で動作し、ほとんどの直接的なデータベース接続ができないため、公式ドキュメントでも対象外とされています。

#### DbContext プーリングと接続プーリングは別物

公式ドキュメントは「**コンテキストプーリングはデータベース接続プーリングとは直交する**」と明言しています。混同しやすいので整理します。

| | DbContext プーリング | 接続プーリング |
| --- | --- | --- |
| 何をプールするか | `DbContext` インスタンス | データベース接続 |
| 誰が管理するか | EF Core | データベースドライバー（`Microsoft.Data.SqlClient` などの ADO.NET プロバイダー） |
| 目的 | インスタンスの割り当てと初期化コストの削減 | 接続の確立・切断コストの削減 |
| 設定方法 | `AddDbContextPool` の `poolSize` | **接続文字列**（`Max Pool Size` など、ドライバーのドキュメントに従う） |
| 既定 | 無効（`AddDbContext` を使うため） | 通常は有効 |

EF Core は接続プーリングを下位のドライバーに任せ、通常は操作の直前に接続を開き、直後に閉じて返却します。次は `IDbConnectionInterceptor` で開閉を確認した例です。同じ `DbContext` の 3 クエリで 3 回、明示的に開く条件では 1 回の開閉を確認できています。

| 操作 | 接続の開閉回数 |
| --- | --- |
| 同一 `DbContext` インスタンスでクエリを 3 回実行 | opened=3 closed=3 |
| `OpenConnectionAsync` で明示的に開いてからクエリを 3 回実行 | opened=1 closed=1 |

`Database.OpenConnectionAsync()` で明示的に開いた場合は、`CloseConnectionAsync()` を呼ぶまで接続が保持されます。ここで注意が必要なのは、**EF Core がリセットするのは `DbContext` とその関連サービスの内部状態だけで、下位のドライバーの状態は元に戻さない** 点です。手動で `DbConnection` を開いたり ADO.NET の状態を操作したりした場合、インスタンスをプールに返す前に元へ戻す責任は利用者側にあります。閉じ忘れると、無関係なリクエストへ状態が漏れる可能性があると公式ドキュメントは警告しています。

> [!TIP]
> **さらに詳しく**
>
> - [付録 EF Core 1：モデル定義（エンティティとリレーションシップ）](../appendix-efcore-01/index.md) — エンティティの構成、リレーションシップ、値の変換、継承のマッピング
> - [付録 EF Core 2：モデル定義（キー・採番・SQL Server 固有）](../appendix-efcore-02/index.md) — キーとインデックス、採番、テンポラルテーブル、SQL Server 固有のマッピング

> [!TIP]
> [公式の DI スコープ検証](https://learn.microsoft.com/ja-jp/dotnet/core/extensions/dependency-injection#scope-validation)では、**Scoped サービスをルートから解決していないか**も確認します。検証有効の確認例では、`app.Services.GetRequiredService<BloggingContext>()` により次の例外を確認できています。検証なしで成功しても正しい寿命になるわけではありません。
>
> ```text
> System.InvalidOperationException: Cannot resolve scoped service
> 'BloggingContext' from root provider.
> ```
>
> ルートコンテナーで作られた Scoped サービスは、アプリケーションの終了時まで破棄されず、実質的に Singleton に昇格してしまうためです。バックグラウンドサービスからは `IServiceScopeFactory` でスコープを作るか、`IDbContextFactory<T>` を使ってください。

#### サードパーティー製プロバイダーはバージョンを実際に確かめる

[公式のプロバイダー一覧](https://learn.microsoft.com/ja-jp/ef/core/providers/)は、**通常、異なる EF Core メジャーバージョンとの互換性はない**と注意しています。サードパーティー製の対応状況は提供元の案内と依存関係も確認してください。次は指定バージョンでの限定した操作の確認例です。復元や操作の成功を、提供元が示す対応範囲の拡張根拠にはしません。

| パッケージ | 今回指定して復元した版 | EF Core 10 のプロジェクトで確認した範囲 |
| --- | --- | --- |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 10.0.3 | EF Core 10.0.11 と PostgreSQL 17.5 の追試で保存・クエリを確認。旧 PostgreSQL 16 の実測は EF Core 10.0.4 の別条件 |
| `Pomelo.EntityFrameworkCore.MySql` | 9.0.0 | **`NU1608` 警告**（下記）。ビルドは通るが、この構成の Context を利用すると例外になる |

```text
warning NU1608: 依存関係の制約外で検出されたパッケージのバージョン:
Pomelo.EntityFrameworkCore.MySql 9.0.0 では
Microsoft.EntityFrameworkCore.Relational (>= 9.0.0 && <= 9.0.999) が必要ですが、
バージョン Microsoft.EntityFrameworkCore.Relational 10.0.11 は解決されました。
```

この警告を無視してこの構成の Context を初期化すると、接続の成功以前に次の型読み込みの例外になります。**ビルドが通ることは動作の保証になりません。**

```text
System.MissingMethodException: Method not found:
'System.String Microsoft.EntityFrameworkCore.Diagnostics.AbstractionsStrings.ArgumentIsEmpty(System.Object)'.
```

つまり **公式一覧だけでも NuGet だけでも判断せず、両方を確認してください。** サードパーティー製プロバイダーを使うプロジェクトでは、EF Core のバージョンをプロバイダーの対応状況に合わせて決めるのが安全です。

#### パッチ版の `Relational` は自分で直接参照する

公式ドキュメントは、リレーショナルプロバイダーを使うときの注意として次を挙げています。EF Core のパッチリリースには `Microsoft.EntityFrameworkCore.Relational` の更新が含まれることが多い一方、**プロバイダーは EF Core とは独立してリリースされるため、新しいパッチ版に依存するよう更新されているとは限らない**、というものです。

NuGet は推移的な依存関係について「条件を満たす最も低いバージョン」を選ぶため、プロバイダーが古いパッチ版を指していると、そのまま古い `Relational` が使われます。次はその確認例です。

```text
# SqlServer 10.0.0 だけを参照した状態
Microsoft.EntityFrameworkCore.SqlServer      10.0.0   10.0.0
Microsoft.EntityFrameworkCore.Relational              10.0.0   ← 推移的
```

`Relational` のパッチ版を直接参照すると、そちらが使われます。

```bash
dotnet add package Microsoft.EntityFrameworkCore.Relational --version 10.0.11
```

```text
Microsoft.EntityFrameworkCore.Relational     10.0.11  10.0.11  ← 直接参照
Microsoft.EntityFrameworkCore.SqlServer      10.0.0   10.0.0
```

公式は、独立してリリースされるプロバイダーの依存指定が追いつかない場合に備え、`Microsoft.EntityFrameworkCore.Relational` のパッチ版をアプリケーションの**直接の依存関係として追加する**ことを推奨しています。ただし、上の混在例は NuGet の解決結果を示すもので、Microsoft 製パッケージを異なるバージョンで運用する推奨構成ではありません。

**Microsoft が提供する `Microsoft.EntityFrameworkCore.*` パッケージは同じバージョンに揃えてください。** これは公式の NuGet パッケージ案内に明記されています。`SqlServer` 自体を 10.0.11 とする確認例では、推移的な `Relational` も 10.0.11 です。`Relational` だけの更新を、プロバイダー自身の修正まで取り込む方法と取り違えないでください。

> [!NOTE]
> 公式は、パッケージのバージョンについて「**NuGet はパッケージバージョンの一貫性を強制しない。参照しているパッケージのバージョンを `.csproj` で必ず注意深く確認すること**」とも警告しています。EF Core 関連のパッケージがすべて同じバージョンになっているかは、`dotnet list package --include-transitive` で確認できます。

---

#### プロバイダーの品質は仕様テストの実装状況で見当をつける

「そのプロバイダーが SQL Server 用プロバイダーと同じように動くか」を判断したいとき、手がかりになるのが **EF Core の仕様テストスイート** です。EF Core は自分のプロバイダー（SQL Server、SQLite、Azure Cosmos DB）の主要な回帰テスト機構として使っているテスト群を、そのまま NuGet パッケージとして公開しています。公式ドキュメントは、すべてのプロバイダーにこれを実装することを推奨しています。

| パッケージ | 反射で数えた属性付きテストメソッド定義数（10.0.11） |
| --- | --- |
| `Microsoft.EntityFrameworkCore.Specification.Tests` | 7,523 |
| `Microsoft.EntityFrameworkCore.Relational.Specification.Tests` | 1,553 |

合計は 9,076 メソッド定義です。`Theory` の入力ごとの実行ケース数やテスト成功件数ではありません。ここで重要なのは、公式ドキュメントが「**テストスイートはかなり大きいので、すべてを実装する必要はない。特定のテストクラスだけを取り込んで、時間をかけて少しずつカバー範囲を広げていくのでまったく問題ない**」と明言していることです。

つまり、**仕様テストを採用しているプロバイダーであっても、どこまで通しているかはプロバイダーごとに違います。** サードパーティー製プロバイダーを採用する際は、リポジトリでこのパッケージを参照しているか、どのテストクラスを取り込んでいるかを確認すると、公式プロバイダーとの機能差を推し量る材料になります。

#### 拡張ライブラリの対応バージョンは一覧より新しいことがある

プロバイダーだけでなく、EF Core の**拡張ライブラリ**も公式ドキュメントに一覧があります。各項目には「対応する EF Core: 5-9」のような対応バージョンが併記されていますが、公式ドキュメント自身が「拡張はさまざまな提供元によって構築されており、EF Core プロジェクトの一部として保守されていない。品質・ライセンス・互換性・サポートなどが要件を満たすか評価すること。特に古いバージョン向けに作られた拡張は、最新バージョンで動かすには更新が必要な場合がある」と注意しています。

次は、EF Core 10 のプロジェクトに指定版を復元した確認例です。2 つの拡張の DDL 生成も確認できていますが、これを互換性・サポートの根拠にはしません。掲載時の一覧の表記と、提供元の対応範囲を区別してください。

| パッケージ | 公式一覧の記載 | 実際に復元されたバージョン |
| --- | --- | --- |
| `EFCore.CheckConstraints` | 5-9 | **10.0.0** |
| `EFCore.NamingConventions` | 3-9 | **10.0.1** |
| `EFCore.BulkExtensions` | 2-8 | **10.0.1** |

指定した 3 パッケージの復元と、`UseSnakeCaseNamingConvention()` / `UseEnumCheckConstraints()` の併用による次の DDL 生成を確認できています。全機能の対応を保証するものではありません。

```sql
-- SQL Server
CREATE TABLE [products] (
    [id] int NOT NULL IDENTITY,
    [name] nvarchar(max) NOT NULL,
    [unit_count] int NOT NULL,
    [kind] int NOT NULL,
    CONSTRAINT [pk_products] PRIMARY KEY ([id]),
    CONSTRAINT [CK_products_kind_Enum] CHECK ([kind] IN (0, 1))
);
```

採用時は提供元が示す対応バージョン、依存関係、ライセンスとサポートを確認します。アプリケーションでの動作確認は、その対応範囲内で必要な操作を確かめるためのものであり、非対応の構成を対応済みと読み替える根拠にはしません。

### Singleton やバックグラウンドサービスから DbContext を使う

Singleton サービスやバックグラウンドサービスから `DbContext` を使う場合は、`IServiceScopeFactory` でスコープを作るか、`IDbContextFactory<T>` を使います。

公式ドキュメントは `BackgroundService` のようなホステッドサービスについて、**スコープ付きの依存関係を直接コンストラクター注入せず、`IServiceScopeFactory` を注入してスコープを作り、そのスコープから解決する**よう案内しています。EF Core 側の公式ドキュメントも、複数のスレッドから使う場合の手段として `IServiceScopeFactory` によるスコープ作成を挙げています。

```csharp
public class ReportWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ReportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // スコープを作り、その中から DbContext を解決する
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BloggingContext>();

        logger.LogInformation(
            "ブログ件数={Count}",
            await context.Blogs.CountAsync(stoppingToken));
    }
}
```

`DbContext` を直接コンストラクターで受け取る形と、この形の実測結果は次のとおりです。

| 実装 | 環境 | 結果 |
| --- | --- | --- |
| `BackgroundService(BloggingContext ctx)` | Development | `AggregateException` → `Cannot consume scoped service 'BloggingContext' from singleton 'Microsoft.Extensions.Hosting.IHostedService'.` |
| 同上 | Production | **例外なく起動してしまう** |
| `IServiceScopeFactory` でスコープを作る | Development | 正常に動作し、クエリが実行された |

```csharp
builder.Services.AddDbContextFactory<BloggingContext>(options =>
    options.UseSqlServer(connectionString));
```

> [!IMPORTANT]
> [公式の `AddDbContextFactory` API](https://learn.microsoft.com/ja-jp/dotnet/api/microsoft.extensions.dependencyinjection.entityframeworkservicecollectionextensions.adddbcontextfactory?view=efcore-10.0)では、既定のファクトリは **Singleton**、コンテキスト型自体も **Scoped** で登録します。ここでの確認例も同じです。直接注入とファクトリ生成を使い分けられますが、**ファクトリ生成のインスタンスは呼び出し側で破棄**してください。

```csharp
public class ReportGenerator(IDbContextFactory<BloggingContext> contextFactory)
{
    public async Task<int> CountBlogsAsync(CancellationToken cancellationToken)
    {
        // ファクトリで作成したインスタンスはアプリケーション側で破棄する
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Blogs.CountAsync(cancellationToken);
    }
}
```

`DbContext` を頻繁に作り直す場合は、プール対応のファクトリから取得できます。`AddPooledDbContextFactory` が再利用するのはファクトリ自体ではなく Context インスタンスです。仕組みは [DbContext プーリング](#dbcontext-プーリングでインスタンスを使い回す)と同じです。

```csharp
builder.Services.AddPooledDbContextFactory<BloggingContext>(options =>
    options.UseSqlServer(connectionString));
```

[公式のプーリングの説明](https://learn.microsoft.com/ja-jp/ef/core/performance/advanced-performance-topics#dbcontext-pooling)では、返却したインスタンスの状態をリセットし再利用します。EF Core 10.0.11 / SQLite で取得と破棄を 4 回逐次繰り返す確認例では、通常のファクトリは別インスタンス、プール対応は同じインスタンスです。後者の再取得時の追跡数は 0 と確認できています。毎回同じインスタンスが返る保証ではありません。

| 登録方法 | 実装型 | 4 回取得したときのインスタンス数 |
| --- | --- | --- |
| `AddDbContextFactory` | `DbContextFactory<T>` | 4 |
| `AddPooledDbContextFactory` | `PooledDbContextFactory<T>` | 1 |

> [!NOTE]
> DI を使わずにプールを直接持つこともできます。`PooledDbContextFactory<T>` は公開型なので、`DbContextOptions` を渡して `new` できます。コンストラクターの `poolSize` は保持するインスタンスの最大数で、既定は 1024 です。上限を超えるとキャッシュされなくなり、要求のたびに生成する非プーリングの動作に戻ります。
>
> なお、プールを使う場合は `AddDbContextPool` と同じ注意が必要です。リクエストごとに変わる状態をフィールドに持たせる設計とは相性が悪くなります。詳しくは「[DbContext プーリングでインスタンスを使い回す](#dbcontext-プーリングでインスタンスを使い回す)」を参照してください。

## 2. クエリとモデルの最適化

### インデックスを正しく張る

クエリの絞り込みや並べ替えに使う列にはインデックスを作成します。

```csharp
// 単一列
modelBuilder.Entity<Post>().HasIndex(p => p.PublishedAt);

// 複合インデックス（列の順序が重要）
modelBuilder.Entity<Post>().HasIndex(p => new { p.BlogId, p.PublishedAt });

// 一意インデックス
modelBuilder.Entity<Blog>().HasIndex(b => b.Url).IsUnique();

// フィルター選択されたインデックス
modelBuilder.Entity<Post>().HasIndex(p => p.Title).HasFilter("[Title] IS NOT NULL");
```

> [!IMPORTANT]
> 適切なインデックスは読み取りを高速化できる一方で、書き込み時のコストとストレージを増やします。すべての列にインデックスを張るのではなく、実際のクエリパターンに基づいて必要なものだけを作成してください。公式ドキュメントは、不要なインデックスを避けるほかに、**インデックスフィルターで対象行を絞り込んでオーバーヘッドを減らす**ことも挙げています。上の `HasFilter` の例は、検索対象が `Title IS NOT NULL` の行だけだとわかっている場合に、インデックスのサイズと更新コストを下げる手段でもあります。

#### インデックスが効く条件と効かない条件

[公式のインデックス利用の指針](https://learn.microsoft.com/ja-jp/ef/core/performance/efficient-querying#use-indexes-properly)は、複合インデックスの列順や、列に式を適用する条件に注意し、実行プランを調べるよう案内しています。次はその補足として、SQL Server 2022 の 20,000 行・統計更新済みのテーブルで、手書きの `SELECT Id` を `SET SHOWPLAN_ALL ON` で確認した結果です。LINQ 欄は条件の対応づけであり、LINQ 生成 SQL の実行結果ではありません。

| 条件に対応する書き方 | 手書き SQL で調べた条件 | 推定プランの物理演算子 |
| --- | --- | --- |
| `Title.StartsWith("T0001")` | `LIKE N'T0001%'` | **Index Seek** |
| `Title.EndsWith("A")` | `LIKE N'%A'` | Index Scan |
| 複合インデックス `(BlogId, Price)` を `BlogId` で絞る | `BlogId = 7` | **Index Seek** |
| 同じインデックスを `Price` **だけ**で絞る | `Price = 7` | Index Scan |
| 同じインデックスを両方で絞る | `BlogId = 7 AND Price = 7` | **Index Seek** |
| 列に対する式で絞る | `Price / 2 = 7` | Index Scan |

公式の指針に沿って、次の点を検討します。

1. **複合インデックスの列順を検索条件に合わせる。** 表のシーク・スキャンはこのデータと SQL の結果です。`Index Scan` もインデックスを読む演算子で、「未使用」という意味ではありません。
2. **列に式を適用する場合は計算列や式インデックスを検討する。** 公式は、永続化された計算列へのインデックスや、対応するデータベースの式インデックスを挙げています。表の `Price / 2 = 7` はスキャンですが、その演算子をすべての環境の固定結果とはしません。

この節と次の付加列のコード例には、本編とは別の `IndexQuerySample` モデルを使います。旧計算列の推定プラン実測には別データベースの `Post2` を使っており、上の手書き SQL の表と同一モデルではありません。以下の `db` は SQL Server を構成した `IndexContext` です。`Price` / `HalfPrice` を本編の `Post` へ追加する必要はありません。

```csharp
using Microsoft.EntityFrameworkCore;

namespace IndexQuerySample;

public class Post
{
    public int Id { get; set; }
    public int BlogId { get; set; }
    public string Title { get; set; } = "";
    public decimal Price { get; set; }
    public decimal HalfPrice { get; set; }
}

public class IndexContext(DbContextOptions<IndexContext> options) : DbContext(options)
{
    public DbSet<Post> Posts => Set<Post>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Post>()
            .Property(p => p.HalfPrice)
            .HasComputedColumnSql("[Price] / 2", stored: true);

        modelBuilder.Entity<Post>().HasIndex(p => p.HalfPrice);
    }
}
```

別データベースの計算列用テーブル `Post2`（20,000 行、`HalfPrice` にインデックスあり）では、`WHERE [HalfPrice] = 7` の **Index Seek** を確認できています。計算列については「[計算列](../appendix-efcore-02/index.md#計算列)」も参照してください。

#### インデックスに列を含める（付加列）

絞り込みには使わないが取得はする列を、インデックスの**キーではない列**としてインデックスに持たせられます。公式ドキュメントは「クエリで使うすべての列がキー列か非キー列としてインデックスに含まれていれば、テーブル自体にアクセスする必要がなくなるため、クエリのパフォーマンスが大きく向上する可能性がある」と説明しています。

次の構成は、直前の `IndexContext.OnModelCreating` の末尾へ追加します。

```csharp
modelBuilder.Entity<Post>()
    .HasIndex(p => p.Price)
    .IncludeProperties(p => p.Title);
```

次は、SQL Server 2022 向けのこのモデルで `GenerateCreateScript()` により確認できている DDL です。

```sql
CREATE INDEX [IX_Posts_Price] ON [Posts] ([Price]) INCLUDE ([Title]);
```

次は、20,000 行の `Posts` から `Price` で絞り込み `Title` と `Price` を取得する条件で、`INCLUDE` の有無を `SET SHOWPLAN_ALL ON` により比較した確認例です。

```csharp
var matches = await db.Posts
    .Where(p => p.Price == 7m)
    .Select(p => new { p.Title, p.Price })
    .ToListAsync(cancellationToken);
```

| インデックスの定義 | 実行プランに現れた物理演算子 |
| --- | --- |
| `([Price])` | Index Seek ＋ **Clustered Index Seek** ＋ **Nested Loops** |
| `([Price]) INCLUDE ([Title])` | **Index Seek のみ** |

この推定プランでは、`INCLUDE` なしの場合は `Title` を取得するキー参照があり、付加列に含める場合はインデックスだけで完結します。クライアントとサーバーの通信往復を比較した結果ではありません。

> [!NOTE]
> 付加列はインデックスのサイズや更新コストに影響します。この確認例ではキー検索なしのプランと結果の一致を確認できていますが、所要時間や I/O の改善率は対象外です。対象クエリに必要な列に絞って指定してください。

#### SQL Server 固有のインデックス構成

インデックスの細かな性質はデータベースごとに異なるため、EF Core はプロバイダー固有の API で構成します。SQL Server プロバイダーでは**クラスター化**と**フィル ファクター**を指定できます。

```csharp
modelBuilder.Entity<Blog>().HasIndex(b => b.PublishedOn).IsClustered();

modelBuilder.Entity<Blog>().HasIndex(b => b.PublishedOn).HasFillFactor(80);
```

次は `IsClustered(false)` と `HasFillFactor(80)` を指定したモデルの `GenerateCreateScript()` の確認結果です。

```sql
CREATE NONCLUSTERED INDEX [IX_Posts_Price] ON [Posts] ([Price]) WITH (FILLFACTOR = 80);
```

> [!NOTE]
> クラスター化インデックスはテーブルごとに 1 つだけです。EF Core は主キーに対してクラスター化インデックスを既定で作成するため、別の列を `IsClustered()` にする場合は主キー側を `IsClustered(false)` にする必要があります。

オンラインで作成する場合は、`HasIndex(...).IsCreatedOnline()` を指定します。SQL Server 2022 Developer Edition で、このモデルからの次の DDL 生成と作成成功を確認できています。

```sql
-- SQL Server
CREATE INDEX [IX_OnlineItems_Name]
ON [OnlineItems] ([Name]) WITH (ONLINE = ON);
```

> [!WARNING]
> [公式のオンライン操作の指針](https://learn.microsoft.com/ja-jp/sql/relational-databases/indexes/guidelines-for-online-index-operations?view=sql-server-ver16)では、終盤に共有ロックやスキーマ変更ロックを保持すると説明しています。**`ONLINE = ON` はロック・待機なしの保証ではありません。** 別トランザクションにスキーマロックを保持させた確認例では SQL エラー 1222、解放後は同じ DDL の成功を確認できています。利用可否はエディションにも依存します。

同じインデックス設定に `SortInTempDb()` と `UseDataCompression(DataCompressionType.Page)` を追加すると、DDL は次のようになります。SQL Server 2022 Developer Edition で、この組み合わせの作成と読み書きを確認できています。

```sql
-- SQL Server
CREATE INDEX [IX_OnlineItems_Name] ON [OnlineItems] ([Name])
WITH (ONLINE = ON, SORT_IN_TEMPDB = ON, DATA_COMPRESSION = PAGE);
```

`DataCompressionType.None` / `Row` / `Page` の 3 条件で、`sys.partitions.data_compression_desc` はそれぞれ `NONE` / `ROW` / `PAGE` と確認できています。**設定の反映の確認**であり、圧縮率や tempdb 使用量、性能改善の測定ではありません。

### 実行プランはデータの量で変わる

公式ドキュメントは、実行プランを分析するときの前提として「**データベースは、実際に入っているデータに応じて異なるクエリプランを生成することがある**。たとえばテーブルに数行しかなければ、インデックスを使わずにテーブル全体をスキャンすることを選ぶかもしれない。**テスト用データベースでプランを分析するなら、本番と似たデータが入っていることを必ず確認すること**」と述べています。ベンチマークについても同じ注意が繰り返されています。

次は SQL Server 2022 CU26 で同じクエリ・インデックスを使い、5 行と 100,005 行の推定プランを比較した結果です。`Filler` は `varchar(8000)` に 8,000 文字、`Rating` は 1〜5 の繰り返しで、両条件とも統計は `FULLSCAN` で更新済みです。列幅や値の分布も関わるため、行数だけで一般化しないでください。経過時間の比較ではありません。

```sql
-- SQL Server（Rating には非クラスター化インデックスがある）
SELECT Id, Rating, Filler FROM Blogs WHERE Rating = 3;
```

| テーブルの状態 | 選ばれた物理演算子 |
| --- | --- |
| 5 行 | `Clustered Index Scan` |
| 100,005 行（`Rating = 3` は 20,001 件） | `Index Seek` + `Key Lookup` |

この条件では、少量側はスキャン、多量側はシークとキー検索です。20% という一致率だけでプランは予測できません。また `Clustered Index Scan` はインデックス不使用を意味しません。

> [!WARNING]
> **数十行しか入っていない開発用データベースで「インデックスが効いている」ことを確認しても、本番の保証にはなりません。** 逆もまた真で、開発環境でスキャンになっていたクエリが本番ではシークになることもあります。インデックスの効果を検証するときは、**行数と値の分布の両方を本番に近づけてから**実行プランを確認してください。統計情報が古いままだと同じことが起きるため、必要に応じて統計を更新し、検証条件を記録してください。統計更新だけで本番と同じプランになる保証はありません。

---

### コレクションのパラメーター化と IN 句の翻訳

`Contains` でコレクションを絞り込み条件に使うと、EF Core はそれを `IN` 句へ変換します。**この変換方法は EF Core 10 で既定値が変わりました。**

```csharp
int[] ids = [1, 2, 3];
var blogs = await context.Blogs.Where(b => ids.Contains(b.Id)).ToListAsync();
```

| バージョン | 既定の翻訳 | 生成される SQL（要点） |
| --- | --- | --- |
| EF Core 8・9 | JSON 配列を 1 つのパラメーターとして送る | `WHERE [b].[Id] IN (SELECT [i].[value] FROM OPENJSON(@ids) WITH (...) AS [i])` |
| EF Core 10 以降 | 要素ごとに個別のパラメーターを送る | `WHERE [b].[Id] IN (@ids1, @ids2, @ids3)` |

以下は EF Core 10.0.11 の SQL Server プロバイダーで、接続を開かずに生成した SQL の比較です。`Parameter` を指定した形は EF Core 8・9 の既定に相当し、配列全体を 1 つのパラメーターで渡して `OPENJSON` で展開します。

```sql
-- EF Core 10 の既定（MultipleParameters）
DECLARE @ids1 int = 1;
DECLARE @ids2 int = 2;
DECLARE @ids3 int = 3;
SELECT [b].[Id], [b].[Name] FROM [Blogs] AS [b]
WHERE [b].[Id] IN (@ids1, @ids2, @ids3)

-- EF Core 9 までの既定（Parameter）
DECLARE @ids nvarchar(4000) = N'[1,2,3]';
SELECT [b].[Id], [b].[Name] FROM [Blogs] AS [b]
WHERE [b].[Id] IN (
    SELECT [i].[value]
    FROM OPENJSON(@ids) WITH ([value] int '$') AS [i]
)
```

[公式の EF Core 10 の説明](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-10.0/whatsnew)では、個別パラメーター方式は件数をプラン選択に利用できる一方、SQL の種類を抑えるためパラメーター数をパディングします。10.0.11 の確認例では、6・7・8・9 要素はいずれも 10 パラメーターの同じ SQL です。具体的なパディング数を将来の版の保証とはせず、プランと実行結果で方式を比較してください。

翻訳方法は `DbContext` 全体でも、クエリ単位でも切り替えられます。

```csharp
// DbContext 全体で切り替える
options.UseSqlServer(connectionString,
    o => o.UseParameterizedCollectionMode(ParameterTranslationMode.Parameter));
```

```csharp
// クエリ単位で切り替える
// JSON 配列パラメーター 1 つ（EF Core 8・9 の既定と同じ形）
await context.Blogs
    .Where(b => EF.Parameter(ids).Contains(b.Id))
    .ToListAsync();

// 定数としてインライン化
await context.Blogs
    .Where(b => EF.Constant(ids).Contains(b.Id))
    .ToListAsync();
```

生成される SQL は次のとおりです（EF Core 10.0.11 / SQL Server プロバイダーでの生成確認。データベースへの発行ではありません）。

```sql
-- 何も指定しない場合（EF Core 10 の既定 = MultipleParameters）
DECLARE @ids1 int = 1; DECLARE @ids2 int = 2; DECLARE @ids3 int = 3;
WHERE [b].[Id] IN (@ids1, @ids2, @ids3)

-- EF.Parameter(ids)
DECLARE @ids nvarchar(4000) = N'[1,2,3]';
WHERE [b].[Id] IN (SELECT [i].[value] FROM OPENJSON(@ids) WITH ([value] int '$') AS [i])

-- EF.Constant(ids)
WHERE [b].[Id] IN (1, 2, 3)
```

> [!NOTE]
> EF Core 10.0.11 には `EF.Parameter`、`EF.Constant` に加えて、リレーショナル側の `EFExtensions` が C# 14 の静的拡張メソッドとして提供する `EF.MultipleParameters` もあります（[公式ソースの宣言](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore.Relational/EFExtensions.cs#L15-L35)）。個別パラメーター方式を基本にするなら全体設定を `MultipleParameters` のままにし、一部のクエリだけ `EF.Parameter` または `EF.Constant` で変更すると、Context の設定をクエリごとに切り替えずに済みます。

| `ParameterTranslationMode` | 動作 |
| --- | --- |
| `MultipleParameters` | 要素ごとのパラメーター。EF Core 10 の既定 |
| `Parameter` | JSON 配列パラメーター 1 つ。EF Core 8・9 の既定 |
| `Constant` | 値を SQL に直接埋め込む。EF Core 7 までの既定 |

> [!TIP]
> 定数方式では値ごとに異なる SQL が生成され、データベース側のプランキャッシュへ影響する可能性があります。EF Core 側のキャッシュとは別です。掲載した 50 回の同じ式の形での確認例は、通常のパラメーター化・`EF.Constant()` ともに 49 ヒット・1 ミスですが、あらゆる式の組み立て方でヒット率が不変という保証ではありません。

### コンパイル済みクエリ

[公式のコンパイル済みクエリの説明](https://learn.microsoft.com/ja-jp/ef/core/performance/advanced-performance-topics#compiled-queries)では、通常のクエリキャッシュでも必要な式ツリーの比較・検索を、デリゲートの直接呼び出しで省くとしています。多くのアプリケーションではこの前処理の影響は小さく、採用前に自分の環境で計測するよう案内されています。

条件付きの補足として、SQLite の 1 件検索を 3,000 回繰り返した確認例では、1 回あたり通常 0.162 ミリ秒、コンパイル済み 0.124 ミリ秒です。この比率を一般的な効果や採用基準にはせず、実際のボトルネックを確認してください。

```csharp
public class BlogQueries
{
    private static readonly Func<BloggingContext, int, IAsyncEnumerable<Blog>> GetBlogsByRating =
        EF.CompileAsyncQuery((BloggingContext context, int rating) =>
            context.Blogs.Where(b => b.Rating == rating));

    public async Task<List<Blog>> FindAsync(BloggingContext context, int rating)
    {
        var results = new List<Blog>();

        await foreach (var blog in GetBlogsByRating(context, rating))
        {
            results.Add(blog);
        }

        return results;
    }
}
```

コンパイル済みクエリは `static` な読み取り専用フィールドとして保持し、アプリケーションの寿命を通じて再利用します。公式ドキュメントは制限として次の 2 点を挙げています。

- 使用できるのは 1 つの EF Core モデルに対してのみです。同じ型の `DbContext` が異なるモデルを使うように構成されている場合、コンパイル済みクエリはサポートされません
- パラメーターには単純なスカラー値を使います。インスタンスのメンバーアクセスやメソッド呼び出しのような、より複雑なパラメーター式はサポートされません

> [!NOTE]
> 公式の制限に従い、引数には**単純なスカラー値**を使います。`Filter` などのオブジェクトを渡してラムダ内でメンバーを読む代わりに、必要なスカラー値を引数として渡してください。

#### コンパイル済みクエリの中では `EF.Constant` と `EF.Parameter` が使えない

[公式の EF Core 9 の破壊的変更](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/breaking-changes#efconstant-and-efparameter-no-longer-work-inside-compiled-queries)では、**`EF.Constant` と `EF.Parameter` はコンパイル済みクエリ内で使用できない**としています。[IN 句の翻訳](#コレクションのパラメーター化と-in-句の翻訳)で使う通常クエリとは区別してください。

公式が挙げる理由は、定数値ごとの再コンパイルを避けるために、これらのメソッドの処理をクエリキャッシュより後の段階へ移し、その実装がコンパイル済みクエリと非互換になったことです。これらの指定が必要なら通常のクエリを使います。

#### 値変換に使うメソッドを private にしてはいけない

[公式の EF Core 9 の破壊的変更](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/breaking-changes#compiled-models-now-reference-value-converter-methods-directly)では、NativeAOT 対応のため、コンパイル済みモデルが**値変換メソッドを直接参照する**ようになったと説明しています。`private` では生成コードのコンパイルが失敗するため、`public` または `internal` にします。

```csharp
public sealed class BooleanToCharConverter()
    : ValueConverter<bool, char>(v => ConvertToChar(v), v => ConvertToBoolean(v))
{
    public static readonly BooleanToCharConverter Default = new();

    private static char ConvertToChar(bool value) => value ? 'Y' : 'N';    // private だと失敗する
    private static bool ConvertToBoolean(char value) => value == 'Y';
}
```

このモデルの確認例では、`dotnet ef dbcontext optimize` は成功し、**生成コードを含むビルドで次の失敗**を確認できています。

```text
CompiledModels/FlaggedEntityType.cs(64,124): error CS0122:
'BooleanToCharConverter.ConvertToChar(bool)' はアクセスできない保護レベルになっています
```

この条件の生成コードでは、メソッドが次のように直接参照されています。

```csharp
string (bool v) => string.Format(CultureInfo.InvariantCulture, "{0}",
    ((object)(BooleanToCharConverter.ConvertToChar(v)))),
```

公式の対処に沿って `internal` に変更した条件では、ビルドの成功を確認できています。

> [!WARNING]
> この対照は、コンパイル済みモデルのビルドと、通常モデルの SQL 生成の確認です。通常モデルでは同メソッドが `private` のまま SQL Server 向けの SQL 生成に成功していますが、保存・再読み込みの確認とは区別してください。

### コンパイル済みモデル

エンティティ数が数百に及ぶ大規模なモデルでは、起動時のモデル構築に時間がかかります。**コンパイル済みモデル** はモデル構築をビルド時に済ませ、起動時間を短縮します。

```bash
dotnet ef dbcontext optimize
```

生成されたモデルを使うように設定します。

```csharp
builder.Services.AddDbContext<BloggingContext>(options =>
    options.UseSqlServer(connectionString)
           .UseModel(BloggingContextModel.Instance));
```

> [!IMPORTANT]
> コンパイル済みモデルにはいくつかの制限があります。グローバルクエリフィルター、遅延読み込みプロキシ、変更追跡プロキシ、カスタムの `IModelCacheKeyFactory` はサポートされません。また、モデルを変更するたびに再生成が必要で、再生成を忘れると実行時に古いモデルが使われます。公式ドキュメントも、小さいモデルでは通常、コンパイルに見合う効果が得にくいと案内しています。エンティティ数だけで判断せず、起動時間を実測し、モデルを再生成・管理する手間も含めて採用を検討してください。
>
> グローバルクエリフィルターを設定したモデルの確認例では、`dotnet ef dbcontext optimize` で次のエラーを確認できています。
>
> ```text
> System.InvalidOperationException: The entity type 'Blog' has a query filter configured.
> Compiled model can't be generated, because query filters are not supported.
> ```
>
> この確認例は生成自体の失敗です。例外の発生段階を一般化するのではなく、公式が示す併用の制限に従ってください。[グローバルクエリフィルター](../appendix-efcore-02/index.md#グローバルクエリフィルターと名前付きクエリフィルター)を使うモデルでは、この制限を確認します。

### NativeAOT と事前コンパイル済みクエリ

.NET の **NativeAOT** は、アプリケーションを事前 (ahead-of-time) にネイティブコードへコンパイルして発行する仕組みです。起動が速く、自己完結した小さなバイナリになります。EF Core もこれに対応するための仕組みを持っていますが、**現時点では実験的機能です。**

> [!WARNING]
> 公式ドキュメントは「NativeAOT とクエリの事前コンパイルはきわめて実験的な機能であり、まだ本番運用に適していない」「将来のバージョンでリリースされる最終的な機能に向けた基盤とみなすべき」と明記しています。**本番環境の EF Core アプリケーションを NativeAOT で発行することは推奨されていません。**

仕組みは **クエリの事前コンパイル (query precompilation)** です。ソースコードを静的に解析して EF Core の LINQ クエリを見つけ、C# の **インターセプター (interceptor)** を生成します。次の確認例でも、生成コードに SQL の埋め込みを確認できています。

```csharp
new RelationalCommand(
    materializerLiftableConstantContext.CommandBuilderDependencies,
    "SELECT \"b\".\"Id\", \"b\".\"Name\"\nFROM \"Blogs\" AS \"b\"\nWHERE \"b\".\"Name\" <> 'foo'\nORDER BY \"b\".\"Id\"",
    ...)
```

有効にするにはプロジェクトファイルに 2 つのプロパティと 1 つのパッケージが要ります。

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
  <InterceptorsNamespaces>$(InterceptorsNamespaces);Microsoft.EntityFrameworkCore.GeneratedInterceptors</InterceptorsNamespaces>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="Microsoft.EntityFrameworkCore.Tasks" Version="10.0.11">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  </PackageReference>
</ItemGroup>
```

`Microsoft.EntityFrameworkCore.Tasks` は MSBuild タスクを提供するパッケージで、`PublishAot` が `true` なら **発行時にコンパイル済みモデルと事前コンパイル済みクエリの生成処理を実行します**。確認記録の `Optimizing DbContext...` は開始ログで、その実行は後続の事前コンパイルで失敗です。開始ログを発行成功と取り違えないでください。生成時期は MSBuild プロパティで制御できます。

| MSBuild プロパティ | 意味 |
| --- | --- |
| `EFScaffoldModelStage` | コンパイル済みモデルを生成する段階。`publish` / `build` / `none`。既定は `publish` |
| `EFPrecompileQueriesStage` | 事前コンパイル済みクエリを生成する段階。同上 |
| `DbContextName` | 対象の `DbContext`。省略するとプロジェクト内のすべてが対象 |
| `EFTargetNamespace` | 生成されるクラスの名前空間。省略時は `$(RootNamespace)` |
| `EFOutputDir` | 生成ファイルの出力先。省略時は `$(IntermediateOutputPath)` |

> [!NOTE]
> このパッケージは推移的な参照にはなりません。**生成されたコードと一緒にコンパイルする必要があるプロジェクトすべてに個別に参照を追加する**必要があります。

発行せずに生成結果だけを確認したい場合は CLI から実行できます。

```bash
dotnet ef dbcontext optimize --precompile-queries --nativeaot
```

この確認例の出力先は、モデルが `CompiledModels/`、インターセプターが `Generated/Program.EFInterceptors.AppDb.cs` です。

#### 最大の制約は「動的クエリが書けない」こと

事前コンパイルは静的解析なので、条件によって演算子を組み立てるクエリは扱えません。

```csharp
// 事前コンパイルできない
IQueryable<Blog> q = db.Blogs.OrderBy(b => b.Id);
if (applyFilter) q = q.Where(b => b.Name != "foo");
return await q.ToListAsync();
```

このコードの確認例では、`dotnet ef dbcontext optimize --precompile-queries` で次のエラーを確認できています。公式が示す非対応条件の補足であり、すべての動的クエリで同じエラー形式になる保証ではありません。

```text
Query precompilation failed with errors:
QueryPrecompilationError { SyntaxNode = q.ToListAsync(),
  Exception = System.InvalidOperationException: Dynamic LINQ queries are not supported when precompiling queries. }
```

公式が案内する対処は、動的な組み立てを **複数の静的なクエリに分解する** ことです。

```csharp
IAsyncEnumerable<Blog> GetBlogs(BlogContext context, bool applyFilter)
    => applyFilter
        ? context.Blogs.OrderBy(b => b.Id).Where(b => b.Name != "foo").AsAsyncEnumerable()
        : context.Blogs.OrderBy(b => b.Id).AsAsyncEnumerable();
```

このほかに、クエリ式構文（`from x in ...` の書き方）が未対応であること、生成されるコードが大きく生成に時間がかかること、キャプチャした状態を使う値変換器が未対応であることが公式に制約として挙げられています。

> [!WARNING]
> [公式の NativeAOT ガイド](https://learn.microsoft.com/ja-jp/ef/core/performance/nativeaot-and-precompiled-queries)は、現状ではトリミング・NativeAOT の警告があり、正常動作を保証できないとしています。また [`EnsureCreated` の API](https://learn.microsoft.com/ja-jp/dotnet/api/microsoft.entityframeworkcore.infrastructure.databasefacade.ensurecreated?view=efcore-10.0)は、設計時モデルを必要とするマイグレーション操作が NativeAOT 非対応で、バンドルなどを使うよう案内しています。同 API を含む確認例でも `IL3050` を確認できています。実験目的にとどめてください。

#### NativeAOT なしで事前コンパイルだけ使う

[公式の NativeAOT なしの事前コンパイル](https://learn.microsoft.com/ja-jp/ef/core/performance/nativeaot-and-precompiled-queries#precompiled-queries-without-nativeaot)では、通常発行でも対応クエリの事前コンパイルを利用できるとしています。次は `PublishAot=false` の確認例です。

```bash
dotnet ef dbcontext optimize --precompile-queries
```

公式は、通常発行では動的クエリなども利用しながら、事前コンパイルできるクエリの起動コストを省けると説明しています。静的クエリだけの最小構成でも、生成・通常発行・実行の成功と、実行時の `QueryCompilationStarting` 0 件を確認できています。この観測は、動的クエリとの併用可否を一般的に制限する根拠ではありません。生成結果と実際の実行経路を確認してください。

## 3. 実行時のコストを下げる

### DbContext プーリングでインスタンスを使い回す

[公式の DbContext プーリング](https://learn.microsoft.com/ja-jp/ef/core/performance/advanced-performance-topics#dbcontext-pooling)では、`AddDbContextPool` によりインスタンスと内部サービスの初期化コストを削減できるとしています。一方、`DbContext` は通常軽量で、多くのアプリケーションでは生成・破棄の影響は小さいと説明しています。採用は実際の処理全体の計測に基づいて判断します。

補足として、取得・`Blogs.Local.Count` へのアクセス・破棄を 3,000 回繰り返す確認例では、1 回あたり通常 0.141 ミリ秒・約 62 KB、プーリングあり 0.0015 ミリ秒・512 バイトです。**データベースへのクエリを含まない**ため、エンドポイント全体の改善率ではありません。

次は、ASP.NET Core から SQL Server の主キー 1 件を読むエンドポイントで、登録だけを入れ替えた確認例です。同時 100 リクエスト、15 秒ずつの条件を示します。

| 登録方法 | スループット（測定が安定したあとの 2 回） |
| --- | --- |
| `AddDbContext` | 8,186 req/s / 8,482 req/s |
| `AddDbContextPool` | 9,506 req/s / 9,550 req/s |

この 2 回の平均では、プーリングありのスループットが約 14% 高い結果です。前のクエリを含まない測定とは指標も処理範囲も異なります。比率や順位をほかの条件へ一般化せず、自分のエンドポイントで評価してください。

```csharp
builder.Services.AddDbContextPool<BloggingContext>(
    options => options.UseSqlServer(connectionString),
    poolSize: 1024);
```

`poolSize` は保持するインスタンスの最大数で、既定は 1024 です。公式は上限を超えるとキャッシュされず、非プーリングの動作に戻ると説明しています。`poolSize: 2` で 5 スコープを同時に保持する確認例でも、上限による待機や例外なしを確認できています。同時実行数を制限する設定ではありません。

> [!WARNING]
> プールされた `DbContext` インスタンスは再利用されるため、実質的に Singleton のように扱われます。`OnConfiguring` は最初の 1 回しか呼ばれず、リクエストごとに変化する状態（テナント ID や現在のユーザーなど）をコンストラクターやフィールドに保持する設計とは相性が悪くなります。そのような場合は、`AddDbContext` を使うか、状態をリセットするフックを実装してください。

### 変更検出のコストを理解する

EF Core の既定は **スナップショット変更追跡 (snapshot change tracking)** です。エンティティを追跡し始めるときに全プロパティの値を内部に複製しておき、保存時にその複製と現在値を比較して変更を洗い出します。この比較を行うのが `ChangeTracker.DetectChanges` で、次のメソッドは結果を正しくするために自動的に呼び出します。

- `SaveChanges` / `SaveChangesAsync`
- `ChangeTracker.Entries()` / `ChangeTracker.Entries<TEntity>()`
- `ChangeTracker.HasChanges()`
- `ChangeTracker.CascadeChanges()`
- `DbSet<TEntity>.Local`

この自動呼び出しは `ChangeTracker.AutoDetectChangesEnabled` で無効にできますが、**安易に触ってはいけません。**

> [!WARNING]
> [公式の変更検出の説明](https://learn.microsoft.com/ja-jp/ef/core/change-tracking/change-detection)では、スナップショット方式で CLR プロパティを直接変更すると、検出処理が必要です。10,000 件中 1 件を書き換え、自動検出なし・明示検出なしで保存する確認例では、**例外なし、影響行数 0** を確認できています。通知方式や EF の状態設定 API を使う場合とは区別してください。
>
> さらに、状態を問い合わせる API も誤った答えを返します。
>
> ```csharp
> context.ChangeTracker.AutoDetectChangesEnabled = false;
> blog.Name = "更新後";
>
> Console.WriteLine(context.Entry(blog).State);            // Unchanged（実際は変更済み）
> Console.WriteLine(context.ChangeTracker.HasChanges());   // False（実際は変更あり）
>
> context.ChangeTracker.DetectChanges();
> Console.WriteLine(context.Entry(blog).State);            // Modified
> Console.WriteLine(await context.SaveChangesAsync());     // ここで初めて 1 行が保存される
> ```
>
> 無効にする場合は `try` / `finally` で元に戻してください。無効な区間でプロパティを変更するなら、必要な時点で明示的に `DetectChanges()` を呼ぶなど、保存前に変更状態が正しく反映されることを保証します。「プロパティを一切書き換えられない」という制限ではありません。

> [!TIP]
> [公式の自動変更検出の指針](https://learn.microsoft.com/ja-jp/ef/core/change-tracking/change-detection#disabling-automatic-change-detection)では、性能のために無効化が必要と決めつけず、プロファイリングで判断します。SQL Server プロバイダー / Release ビルド / 3 プロパティの 10,000 件を追跡する補足例は、読み込み直後・変更前の `DetectChanges()` 1 回が 6 ミリ秒です。取得時間を含まず、件数だけでコストを判断する根拠にもなりません。

エンティティ数が多く、変更検出がボトルネックなら、[公式の変更追跡プロキシ](https://learn.microsoft.com/ja-jp/ef/core/change-tracking/change-detection#change-tracking-proxies)も選択肢です。`Microsoft.EntityFrameworkCore.Proxies` と `UseChangeTrackingProxies()` により、通知インターフェイスを持つ派生型を動的に生成します。この確認例でも、`AutoDetectChangesEnabled = false` でプロパティを変更した直後に `Modified` を確認できています。

導入時には、次の制約を確認してください。

> [!WARNING]
> 公式は、**常にプロキシのインスタンスを追跡する**よう求めています。元の型は必要な通知を出さず、変更を見逃すためです。新しいエンティティは `new` ではなく `CreateProxy` で作ります。次の例外は元の型を `Add` する確認例の結果で、誤用が必ず例外で検出される保証ではありません。
>
> ```csharp
> // 通知を生成するプロキシのインスタンスを作る
> var blog = context.CreateProxy<Blog>(b => { b.Name = "新しいブログ"; });
> context.Blogs.Add(blog);
> ```
>
> ```text
> InvalidOperationException: The entity type 'Blog' is configured to use the
> 'ChangingAndChangedNotifications' change tracking strategy, but does not implement
> the required 'INotifyPropertyChanging' interface.
> ```
>
> 加えて、公式は継承可能な型とオーバーライド可能なプロパティを必要条件に挙げています。次は `Id` が `virtual` でない構成での確認例です。例外の時点・型を、すべての条件違反に共通する仕様とはしません。
>
> ```text
> InvalidOperationException: Property 'Blog.Id' is not virtual. 'UseChangeTrackingProxies'
> requires all entity types to be public, unsealed, have virtual properties, and have a
> public or protected constructor. 'UseLazyLoadingProxies' requires only the navigation
> properties be virtual.
> ```
>
> **遅延読み込みプロキシでは対象のナビゲーション、変更追跡プロキシでは変更を通知するマップ済みプロパティがオーバーライド可能である必要があります。** 上のメッセージだけで判断せず、それぞれの[公式の変更追跡](https://learn.microsoft.com/ja-jp/ef/core/change-tracking/change-detection#change-tracking-proxies)・[遅延読み込み](https://learn.microsoft.com/ja-jp/ef/core/querying/related-data/lazy#lazy-loading-with-proxies)の条件に従ってください。

#### 通知を自分で実装する

変更追跡プロキシは動的な派生型を作るため、エンティティのプロパティを `virtual` にする、コンストラクターに制約が付くといった条件が付きます。**プロキシを使わずに、自分で `INotifyPropertyChanged` / `INotifyPropertyChanging` を実装する**こともできます。

```csharp
public class Blog : INotifyPropertyChanged, INotifyPropertyChanging
{
    private string _name = "";

    public string Name
    {
        get => _name;
        set
        {
            PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(nameof(Name)));
            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event PropertyChangingEventHandler? PropertyChanging;
}
```

重要なのは、**インターフェイスを実装しただけでは EF Core は通知イベントを購読しない**という点です。公式ドキュメントは「EF Core はこれらのインターフェイスが正しく実装されているかを検証できないため、自動的にはイベントを購読しない」と明記しています。モデル側で戦略を指定する必要があります。

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
    => modelBuilder.HasChangeTrackingStrategy(
        ChangeTrackingStrategy.ChangedNotifications);
```

次は 3 パターンの確認例です。いずれも `AutoDetectChangesEnabled = false` で、読み込んだエンティティのプロパティを書き換えた直後の状態です。

| エンティティ | 書き換えた直後の状態 |
| --- | --- |
| 通常のエンティティ | `Unchanged`（`DetectChanges` を呼ぶと `Modified`） |
| 通知エンティティ + 戦略を構成した | `Modified`（`DetectChanges` は不要） |
| 通知エンティティ + 戦略を構成しない | `Unchanged` |

3 つ目では通知を利用する戦略を構成していないため、通知イベントによる変更追跡は行われません。ただし既定のスナップショット方式まで無効になるわけではありません。この測定は自動検出も無効にしているので直後は `Unchanged` ですが、必要な変更検出を行えば状態に反映できます。

戦略は 4 つあり、公式ドキュメントは次のように整理しています。

| `ChangeTrackingStrategy` | 必要なインターフェイス | `DetectChanges` が必要か | 元の値をスナップショットするか |
| --- | --- | --- | --- |
| `Snapshot`（既定） | なし | 必要 | する |
| `ChangedNotifications` | `INotifyPropertyChanged` | 不要 | する |
| `ChangingAndChangedNotifications` | 上記 + `INotifyPropertyChanging` | 不要 | しない |
| `ChangingAndChangedNotificationsWithOriginalValues` | 上記 + `INotifyPropertyChanging` | 不要 | する |

`ChangedNotifications` でも元の値は保持されます。この確認例でも、書き換え直後の `OriginalValue` に元の値を確認できています。スナップショットまで省く `ChangingAndChangedNotifications` には、`INotifyPropertyChanging` の実装が必要です。

> [!WARNING]
> 公式ドキュメントは、EF Core が要求するのは**すべてのプロパティ（ナビゲーションを含む）の通知**だと述べています。一部のプロパティだけ通知する実装は、EF Core との組み合わせでは正しく動きません。
>
> また、エンティティ型ごとに戦略を変えることもできますが、公式ドキュメントは「通知エンティティでない型のために結局 `DetectChanges` が必要になるので、たいていは逆効果」としています。

### バッファリングとストリーミング

`ToListAsync` は結果をすべてメモリに読み込みます（バッファリング）。大量の行を順次処理するだけなら、`await foreach` によるストリーミングでメモリ使用量を抑えられます。

[公式のバッファリングとストリーミング](https://learn.microsoft.com/ja-jp/ef/core/performance/efficient-querying#buffering-and-streaming)は、結果全体を保持するか順次処理するかの違いを説明しています。SQLite の 200,000 行（`Name` は 200 文字）で同期の `ToList` と `AsEnumerable` を比較した補足例では、`GC.GetTotalMemory` を約 1 ミリ秒間隔で読むヒープ増加量の最大値は +121 MiB と +6 MiB です。プロセス全体のピークメモリではなく、この比率をほかのデータ量や処理へ適用することもできません。

```csharp
await foreach (var post in context.Posts.AsNoTracking().AsAsyncEnumerable()
    .WithCancellation(cancellationToken))
{
    await ProcessAsync(post, cancellationToken);
}
```

> [!IMPORTANT]
> 結果をデータベースから読み取っている間は、接続やデータリーダーを保持します。ただし内部バッファリングにより、アプリケーションが最初のエンティティを受け取る前に物理リーダーが閉じる場合もあります。別クエリを同時に実行できるかはプロバイダーなどの条件にも依存するため、同じ `DbContext` での操作を重ねず、列挙ループ中の別クエリを避ける設計にしてください。
>
> また、`EnableRetryOnFailure` を有効にしている場合、公式ドキュメントは「再試行を有効にすると EF が結果セットを内部でバッファリングするため、大量の行を返すクエリではメモリ使用量が大きく増える可能性がある」と明記しています（[接続の回復性](https://learn.microsoft.com/ja-jp/ef/core/miscellaneous/connection-resiliency)）。つまり、再試行と併用すると、ストリーミングだけで結果セットの内部バッファーをなくすことはできません。
>
> 次はその補足例です。SQL Server の 100,000 行（`Name` は 200 文字、`Url` は 1 文字）を `AsAsyncEnumerable` で列挙した条件のヒープ増加量を示します。
>
> | `EnableRetryOnFailure` | マネージドヒープ増加量の最大値 |
> | --- | --- |
> | 無効 | +7 MiB |
> | 有効 | +97 MiB |
>
> この表は `AsAsyncEnumerable` で列挙するときの再試行の有無を比較したもので、`ToList` とのメモリ使用量の差を測ったものではありません。内部バッファーがあることから、ストリーミングと `ToList` のメモリ使用量が常に同じだとは結論できません。
>
> [公式の内部バッファリングの説明](https://learn.microsoft.com/ja-jp/ef/core/performance/efficient-querying#internal-buffering-by-ef)では、再試行時に同じ結果を返せるよう結果を保持します。**メモリ使用量だけでなく接続の回復性も評価する必要があります。** この測定の倍率から再試行の無効化を勧めることはできません。まず取得列や件数、ページングを見直し、必要な回復性と実際のメモリ使用量を併せて判断してください。

> [!WARNING]
> **分割クエリでも EF Core は内部でバッファリングします。** 公式ドキュメントは、EF が結果セットを内部でバッファリングするケースとして再試行実行戦略と分割クエリの 2 つを挙げ、分割クエリでは「**SQL Server で MARS が有効になっていない限り、最後のクエリ以外のすべての結果セットがバッファリングされる**」と説明しています。複数の結果セットを同時に開いておくことが通常できないためです。
>
> ブログ 200 件・投稿 50,000 件（各投稿の `Body` は 240 文字）を `AsSplitQuery()` と `AsAsyncEnumerable()` で列挙し、**1 件目を受け取った時点**のマネージドヒープ増加量を `GC.GetTotalMemory` で測った結果です。全列挙中の最大値を測ったものではありません。
>
> | 接続文字列 | 1 件目受信時のメモリ増加量 |
> | --- | --- |
> | 既定（MARS 無効） | **+58 MiB** |
> | `MultipleActiveResultSets=True` | +1 MiB |
>
> MARS 無効のこの条件では、1 件目の受信時点で +58 MiB を確認できています。**分割クエリをストリーミングすれば、常に小さいメモリで処理できるわけではありません。**
>
> なお、この内部バッファリングは LINQ 演算子によるバッファリングとは別に発生します。公式ドキュメントも「再試行実行戦略が有効な状態で `ToList` を使うと、結果セットは**メモリに 2 回読み込まれる**（EF による内部の 1 回と `ToList` による 1 回）」と注意しています。

### 非同期 API を使う

同期版の `ToList()` や `SaveChanges()` は、データベースの応答を待つ間スレッドをブロックします。ASP.NET Core ではスレッドプールが枯渇し、スループットが大きく低下する原因になります。原則として非同期版を使い、`.Result` や `.Wait()` によるブロッキングを避けてください。公式ドキュメントも、**同期と非同期のコードを同じアプリケーションで混在させない**よう警告しています。気づかないうちにスレッドプールの枯渇を招きやすいためです。

次は同期・非同期のエンドポイントで同じ SQL Server クエリを使う確認例です。`WAITFOR DELAY '00:00:00.100'` で 100 ミリ秒の待機を含め、10 コアのマシンで同時リクエスト数を変えています。5 回のウォームアップ後、15 秒間要求を開始し、送信済み要求の完了までを含む総経過時間からスループットを求めた結果です。

| 同時リクエスト数 | 非同期のスループット | 同期のスループット | 非同期の p99 | 同期の p99 |
| --- | --- | --- | --- | --- |
| 10 | 89 req/s | 89 req/s | 144 ミリ秒 | 135 ミリ秒 |
| 50 | 427 req/s | 123 req/s | 141 ミリ秒 | 767 ミリ秒 |
| 100 | 948 req/s | 129 req/s | 114 ミリ秒 | 1,327 ミリ秒 |
| 200 | 1,862 req/s | 107 req/s | 118 ミリ秒 | 2,958 ミリ秒 |

この確認例では、同時 10 のスループットは同程度、同時 50〜200 は非同期版が高い結果です。同時 200 の p99 は非同期 118 ミリ秒、同期 2,958 ミリ秒で、エラーは 0 件です。**順位・倍率や差が現れる同時実行数を、他のアプリケーションへ一般化しないでください。**

公式ドキュメントはこの理由を「同期 API はデータベースの入出力の間スレッドをブロックするため、必要なスレッド数と、発生するスレッドのコンテキストスイッチの回数が増える」と説明しています。

> [!WARNING]
> **例外があります。** EF Core の公式パフォーマンスガイダンスは、SQL Server 用のドライバーである `Microsoft.Data.SqlClient` の非同期実装に**既知の問題がある**ことを明記しています。原因不明の性能問題が出ている場合、**特に大きなテキストやバイナリの値を扱うときは、同期のコマンド実行を試してみる**よう案内しています。公式が挙げている Issue は [dotnet/SqlClient#593](https://github.com/dotnet/SqlClient/issues/593)（大きなデータの非同期読み取りが極端に遅い）と [dotnet/SqlClient#601](https://github.com/dotnet/SqlClient/issues/601) で、**いずれも本書の執筆時点で未解決 (Open) のままです。**
>
> 次は Azure 上の SQL Server 2022 で、4 MiB の `varbinary(max)` を 20 行（計 80 MiB）、`AsNoTracking()` で読み取る条件の補足例です。
>
> | 読み取る内容 | 同期 `ToList()` | 非同期 `ToListAsync()` |
> | --- | --- | --- |
> | 4 MiB のバイナリ × 20 行（80 MiB） | 1,623 / 2,029 / 1,955 ミリ秒 | 2,327 / 2,515 / 2,599 ミリ秒 |
> | 短い文字列の列だけを投影 | 9 ミリ秒 | 7 ミリ秒 |
>
> この条件ではバイナリの 3 回とも非同期が遅く、時間の比は 1.2〜1.4 倍です。短い文字列だけの投影は同期 9 ミリ秒、非同期 7 ミリ秒です。いずれも他のクエリ・環境での順位や性能差の根拠にはなりません。
>
> ただしこれは「非同期をやめてよい」という意味ではありません。同期版はスレッドをブロックするため、**Web アプリケーションでは既定を非同期のままにしてください。** 大きな BLOB を扱う特定のクエリで実測して差が大きい場合に限り、その箇所だけ同期実行を検討するというのが公式の趣旨です。なお測定値はネットワーク遅延を含む環境依存の値なので、必ず自分の環境で計測してから判断してください。

> [!NOTE]
> **Azure Cosmos DB は同期 I/O に対応せず、EF Core の同期 API は既定で例外になります。** Cosmos DB プロバイダーが内部で使う SDK は同期入出力を提供していません。EF Core 9.0 以降は、この利用を検出すると既定で例外にします。
>
> 次は実 Azure の Cosmos DB（NoSQL API）と EF Core 10 での確認例です。保存は新しい `Order` を追加した条件で、例外欄は型と要旨です。外側の例外型は、この経路での観測として扱ってください。
>
> | 呼び出し | 結果 |
> | --- | --- |
> | `SaveChangesAsync()` / `ToListAsync()` | 成功 |
> | `ToList()` | `SyncNotSupported` 警告を例外化した `InvalidOperationException`。メッセージ中に `Azure Cosmos DB does not support synchronous I/O.` を含む |
> | `SaveChanges()` | `DbUpdateException`（内部例外が同じ `InvalidOperationException`） |
>
> 以前の EF Core は非同期処理を内部で同期的に待機していました。公式はこの「sync over async」をデッドロックを招きうる非推奨の手法としています。警告設定によって例外を抑制できても同期 I/O が提供されるわけではありません。SQL Server の同期 API と同じ前提にせず、Cosmos DB では正しく `await` する非同期 API を使ってください。

> [!NOTE]
> **キャンセルトークンを渡すことと、処理の停止・保存状態の確認は別です。** EF Core は `CancellationToken` を下位のプロバイダーへ渡しますが、尊重されるかどうかはプロバイダーによると[公式の非同期ガイド](https://learn.microsoft.com/ja-jp/ef/core/miscellaneous/async)に明記されています。
>
> [SqlClient 6.1.6 の公式の非同期キャンセル試験](https://github.com/dotnet/SqlClient/blob/v6.1.6/src/Microsoft.Data.SqlClient/tests/ManualTests/SQL/SqlCommand/SqlCommandCancelTest.cs#L534-L560)は、待機終了より前の例外終了を検証しています。EF Core 10.0.11、同ドライバー、SQL Server 2022 の 20 秒の `WAITFOR` の確認例でも、開始前キャンセル・待機中キャンセルの両方で時間内の例外終了を確認できています。例外型や停止時間が全経路で同じという意味ではありません。
>
> この確認例は、明示トランザクションで先に 1 行を更新する構成です。例外終了後に `CancellationToken.None` で `RollbackAsync` と破棄を完了した状態で、別の物理接続から元の値を確認できています。**キャンセル要求だけでロールバックまで完了するという結果ではありません。**

### プロバイダーを替えるとモデルの意味が変わる

プロバイダーの差し替えは「接続先が変わるだけ」ではありません。Azure Cosmos DB のようなドキュメントデータベースでは、**同じ C# のモデルが別の意味に解釈されます。**

[公式の Cosmos DB モデリング](https://learn.microsoft.com/ja-jp/ef/core/providers/cosmos/modeling)では、関連する型は既定で所有型として親文書に埋め込み、独立型にする場合は `ModelBuilder.Entity` で明示すると説明しています。一方、[公式の制限事項](https://learn.microsoft.com/ja-jp/ef/core/providers/cosmos/limitations)では、別ドキュメント間の関連グラフの読み込みは非対応です。

次の確認例は、`string` の `Id` を持つ最小の `Blog` / `Post` と、`DbSet<Blog>` だけを公開する Context を使います。ブログ・投稿各 1 件を保存し、新しい Context で読む条件です。独立型では `b.Entity<Post>()` と `HasMany(...).WithOne().HasForeignKey("BlogId")` を指定しています。本編の接続先だけを変更した結果ではありません。

| モデルの構成 | `Post.IsOwned()` | `Post` のコンテナー | `Include()` | `Include` なしで読んだ子 |
| --- | --- | --- | --- | --- |
| 既定（何も書かない） | `True` | なし（親に埋め込み） | 成功（そもそも不要） | **1 件** |
| `b.Entity<Post>()` と関係を明示 | `False` | `Db` | **例外** | 0 件 |

既定のままなら、`Include` を書かなくても子は一緒に読み込まれます。1 つのドキュメントとして格納されているためです。

独立型を明示したこの確認例では、別文書の `Include` で次の例外を確認できています。

```text
InvalidOperationException: Including navigation 'Navigation: Blog.Posts (List<Post>)
Collection ToDependent Post' is not supported as the navigation is not embedded in same resource.
```

公式ドキュメントは、**別ドキュメントから関連エンティティのグラフを読み込むことはサポートしない**と説明しています。上の独立型の例外はこの制限に対応します。親と同じドキュメントに埋め込まれた所有型への `Include` まで一律に失敗するという意味ではありません。

> [!WARNING]
> リレーショナル向けのモデルやクエリを、そのまま Cosmos DB へ移植できるとは限りません。**集約とドキュメントの単位、パーティションキー、関連データの読み方**を見直してください。必要な変更範囲は元のモデルによって異なります。

---

### Cosmos DB の接続と保存で注意すること

以下は `Microsoft.EntityFrameworkCore.Cosmos` 10.0.11、依存する Cosmos SDK 3.61.0 と、実 Azure の NoSQL アカウントで確認した内容です。SQL Server 向けの接続設定や同時実行制御をそのまま当てはめないでください。

#### DI 登録の引数と接続モード

[公式の `AddCosmos<TContext>` API](https://learn.microsoft.com/ja-jp/dotnet/api/microsoft.extensions.dependencyinjection.cosmosservicecollectionextensions.addcosmos?view=efcore-10.0)の 2 つの文字列は、**接続文字列とデータベース名**です。この確認例でも正しい引数による Scoped の解決・読み取りと、エンドポイントを接続文字列の位置へ渡す誤用の `ArgumentException` を確認できています。エンドポイントとキーを個別に渡す場合は `UseCosmos(endpoint, key, databaseName)` を使い、接続値をソースコードへ埋め込まないでください。

公式の[Cosmos DB プロバイダーの接続オプション](https://learn.microsoft.com/ja-jp/ef/core/providers/cosmos/#azure-cosmos-db-options)には、一覧中のオプションを**すべて同時に使う意図ではない**と注意があります。

| 構成 | 実測結果と注意 |
| --- | --- |
| Gateway に Direct 専用の TCP 設定を指定 | `MaxRequestsPerTcpConnection` などの指定で `ArgumentException`。接続モードに適した設定だけを使う |
| `HttpClientFactory` と `GatewayModeMaxConnectionLimit` を併用 | 明示的な上限指定との併用で `ArgumentException`。ファクトリを使う場合の上限は `HttpClientHandler.MaxConnectionsPerServer` で構成する |
| Direct の `IdleTcpConnectionTimeout` を 1 分に設定 | 設定値の読み返しは成功しても、初回の入出力で `ArgumentOutOfRangeException`。公式下限の 10 分に直すと読み取りに成功 |

Gateway・Direct の両方で実通信を確認できていますが、接続上限までの負荷や回収時刻の測定ではありません。**設定値を読み返せることと通信の成功は別です。**

#### コンテナーとパーティションキー

[公式のモデリングガイド](https://learn.microsoft.com/ja-jp/ef/core/providers/cosmos/modeling)では、`HasDefaultContainer` は既定コンテナー、`ToContainer` は型ごとの保存先、`ToJsonProperty` は JSON 名を指定します。この確認例でも保存先と JSON 名の反映を確認できています。

[公式のクエリガイド](https://learn.microsoft.com/ja-jp/ef/core/providers/cosmos/querying)では、項目の識別に id とパーティションキーを使い、`WithPartitionKey` で検索先を限定します。単一キーの確認例でも、同じ `id` を異なるパーティションへ保存し、両値を渡した `FindAsync` による識別を確認できています。

階層パーティションキーは、`HasPartitionKey(x => new { x.TenantId, x.UserId, x.SessionId })` のように構成します。`string` / `Guid` / `int` の 3 階層・同じ id の 4 文書の確認例では、id と全階層のキーを渡す `FindAsync` により目的の 1 件のポイント読み取りを確認できています。

公式は、LINQ からパーティションキーの等値条件を取り出す最適化を説明しています。先頭から連続する 1 個・2 個・全 3 個の確認例では、値を実行ログの `Partition` に確認できています。中間・末尾だけの条件は SQL 側に残る結果であり、先頭キーの有無を無視して同じ検索先に限定できるとは考えないでください。

#### ETag と SDK 経由の更新

[公式の ETag による楽観的同時実行制御](https://learn.microsoft.com/ja-jp/ef/core/providers/cosmos/modeling#optimistic-concurrency-with-etags)では、シャドウ状態に `UseETagConcurrency()`、CLR プロパティへのマッピングに `IsETagConcurrency()` を使います。両方式の確認例でも、別 Context の先行更新後に古い ETag で保存すると、HTTP 412 に基づく `DbUpdateConcurrencyException` を確認できています。

[公式の ID 解決](https://learn.microsoft.com/ja-jp/ef/core/change-tracking/identity-resolution#identity-resolution-and-queries)では、追跡済みインスタンスの再利用時にデータベースの値で上書きしません。SDK で文書を更新する確認例でも、既存の追跡値は古いまま、新しい `DbContext` は更新後の値です。`GetCosmosClient()` を使うだけで追跡状態も同期されるわけではありません。

#### 有効期限とスループットの構成

[公式の TTL (Time to Live)](https://learn.microsoft.com/ja-jp/azure/cosmos-db/time-to-live)は最終更新からの有効期間を秒数で指定し、`-1` は無効化です。コンテナーの既定と項目の `ttl` を分けます。既定 8 秒・個別 4 秒・個別 `-1` の確認例では、11 秒後は前の 2 条件が HTTP 404、`-1` は取得可能です。物理削除の完了時刻の測定ではありません。

スループットも、データベース共有とコンテナー専用では構成対象が異なります。新規作成の確認例では、`ModelBuilder` の `HasManualThroughput(400)` / `HasAutoscaleThroughput(1000)` で共有の手動 400 RU/s / 自動最大 1000 RU/s、エンティティ側の `HasManualThroughput(500)` で専用の手動 500 RU/s を確認できています。構成値の確認であり、負荷に応じたスケールや性能の測定ではありません。

#### トリガーは全クライアントに強制されない

[公式のデータベーストリガー](https://learn.microsoft.com/ja-jp/ef/core/providers/cosmos/modeling#database-triggers)は、EF Core 10 の `HasTrigger` による Pre / Post と操作種別の指定を説明しています。この確認例でも、Pre の Create / Replace による書き換えと、Post の Delete での例外に伴うロールバックを確認できています。

> [!WARNING]
> **公式は、直接アクセスするクライアントがトリガーを省略できるため、認証や監査の強制に使わないよう警告しています。** この確認例でも、SDK から指定せずに操作した場合の成功を確認できています。SQL Server のトリガーと同じ強制力を想定しないでください。

### Cosmos DB の全文検索とベクトル検索

Cosmos DB 用の EF Core 10 は、**全文検索、ベクトル検索、および両方の順位を組み合わせるハイブリッド検索**をサポートします。SQL Server の `Contains` / `FreeText` や `SqlVector<T>` とは別の API です。ここでは `Microsoft.EntityFrameworkCore.Cosmos` 10.0.11 と、ベクトル検索を有効にした実 Azure アカウントで確認した例を示します。

#### モデルに検索ポリシーと索引を設定する

以下はこの節専用のエンティティと、`UseCosmos` で接続先を設定したコンテキストの `OnModelCreating` 内の抜粋です。`DistanceFunction` と `VectorIndexType` は `Microsoft.Azure.Cosmos` 名前空間の型です。

```csharp
public class Blog
{
    public string Id { get; set; } = "";
    public string Partition { get; set; } = "p";
    public string Contents { get; set; } = "";
    public float[] Vector { get; set; } = [];
}
```

```csharp
modelBuilder.Entity<Blog>(b =>
{
    b.ToContainer("basic");
    b.HasPartitionKey(x => x.Partition);
    b.HasManualThroughput(400);
    b.Property(x => x.Contents).EnableFullTextSearch();
    b.HasIndex(x => x.Contents).IsFullTextIndex();
    b.Property(x => x.Vector)
        .IsVectorProperty(Microsoft.Azure.Cosmos.DistanceFunction.Cosine, dimensions: 3);
    b.HasIndex(x => x.Vector)
        .IsVectorIndex(Microsoft.Azure.Cosmos.VectorIndexType.Flat);
});
```

この新規コンテナーの確認例では、本文と 3 次元の `float[]` を持つ 6 文書の保存・検索・再取得を確認できています。全文検索・ベクトル検索用の設定で、既存コンテナーのポリシーを自動更新する手順ではありません。作成時の認証は[付録3のマイグレーションの制限](../appendix-efcore-03/index.md#1-マイグレーションの詳細)を参照してください。

> [!WARNING]
> [公式サービスガイド](https://learn.microsoft.com/ja-jp/azure/cosmos-db/vector-search)の **`Flat` 索引の上限は 505 次元**です。505 次元の成功、506・1536 次元での作成時 HTTP 400 を確認できています。例示コードよりサービスの制限に従ってください。`QuantizedFlat` の 1536 次元も作成・検索成功を確認できていますが、6 文書での結果は大規模データの性能や近似索引の利用を示すものではありません。

#### 全文検索と関連度による順位付け

全文検索で使う API と、6 文書のデータで確認した用途・結果を整理します。

| API | 用途 | 6 文書での実測例 |
| --- | --- | --- |
| `FullTextContains` | キーワードやフレーズで絞り込む | `database` が 3 件に一致 |
| `FullTextContainsAll` | 指定した全キーワードを含む | `database` と `cosmos` が 2 件に一致 |
| `FullTextContainsAny` | いずれかのキーワードを含む | `cosmos` または `bicycle` が 4 件に一致 |
| `FullTextScore` | BM25 による関連度順に並べる | `OrderBy` と `Take` で取得 |

```csharp
string[] keywords = ["database", "cosmos"];
var textResults = await db.Blogs
    .OrderBy(x => EF.Functions.FullTextScore(x.Contents, keywords))
    .Take(5)
    .ToListAsync();
```

[公式の全文検索ガイド](https://learn.microsoft.com/ja-jp/ef/core/providers/cosmos/full-text-search)は、`FullTextScore` の用途を並べ替えに限定しています。この確認例でも `Select` への投影・`Where` 条件で HTTP 400 を確認できています。通常の数値計算メソッドとは異なります。

既定言語は `en-US` です。[公式 API](https://learn.microsoft.com/ja-jp/dotnet/api/microsoft.entityframeworkcore.cosmosmodelbuilderextensions.hasdefaultfulltextlanguage?view=efcore-10.0)の既定言語の設定先は `ModelBuilder` です。`EnableFullTextSearch("de-DE")` と `modelBuilder.HasDefaultFullTextLanguage("de-DE")` の確認例も成功ですが、[多言語対応の公式条件](https://learn.microsoft.com/ja-jp/azure/cosmos-db/gen-ai/full-text-search)の代わりにはなりません。全言語・全リージョンでの利用保証ではありません。

#### ベクトル検索とハイブリッド検索

```csharp
float[] queryVector = [1, 0, 0];
var vectorResults = await db.Blogs
    .OrderBy(x => EF.Functions.VectorDistance(x.Vector, queryVector))
    .Take(5)
    .ToListAsync();

var hybridResults = await db.Blogs
    .OrderBy(x => EF.Functions.Rrf(
        new[]
        {
            EF.Functions.FullTextScore(x.Contents, "database"),
            EF.Functions.VectorDistance(x.Vector, queryVector)
        },
        weights: new double[] { 1, 2 }))
    .Take(5)
    .ToListAsync();
```

[公式のハイブリッド検索](https://learn.microsoft.com/ja-jp/ef/core/providers/cosmos/full-text-search)では、**RRF (Reciprocal Rank Fusion)** で複数の順位を統合します。上の式を Cosmos DB の検索へ変換でき、この確認例でも重みに応じた上位文書の違いを確認できています。

> [!NOTE]
> [公式の `Rrf` API](https://learn.microsoft.com/ja-jp/dotnet/api/microsoft.entityframeworkcore.cosmosdbfunctionsextensions.rrf?view=efcore-10.0)は、重みの型を **`double[]`** と定義しています。`new[] { 1, 2 }` は `int[]` のため使えません。得点・重みの配列による確認例は問い合わせの動作に限り、AI の回答精度や大規模データの性能向上を示すものではありません。

---

### 同時実行検出を無効にしてはいけない

1 つの `DbContext` インスタンスで操作が重複すると、EF Core の **同時実行検出 (concurrency detection)** によって `InvalidOperationException` が発生することがあります。未検出なら安全という意味ではありません。この検出は `DbContextOptionsBuilder.EnableThreadSafetyChecks(false)` で無効にできます。公式ドキュメントは「わずかな性能向上が得られるが、`DbContext` インスタンスが同時に使われた場合の **動作は未定義になり、プログラムは予測できない形で失敗する可能性がある**」と説明し、「性能向上が相当なものであることを確認し、アプリケーションを同時実行のバグについて十分にテストしたうえでのみ無効化すること」と釘を刺しています。

次は SQL Server 2022 で、同じ `DbContext` から 2 本のクエリを `Task.Run` で並行実行し、検出の有無を変えて各 3 回確認した結果です。例外欄は先頭行または要旨で、完全なスタックトレースではありません。

| 同時実行検出 | 発生した例外 |
| --- | --- |
| 有効（既定） | 3 回とも `InvalidOperationException: A second operation was started on this context instance before a previous operation completed.`（原因と対処ページへのリンク付き） |
| 無効 | 1 回目: `InvalidOperationException`（接続が閉じられていない旨）<br>2 回目: `InvalidOperationException`（同上、接続状態の表示だけが異なる）<br>3 回目: `InvalidCastException: Unable to cast object of type 'Microsoft.Data.ProviderBase.DbConnectionClosedConnecting' to type 'Microsoft.Data.SqlClient.SqlInternalConnectionTds'.` |

この 3 回で確認できているのは接続状態や型変換の例外であり、実行ごとに異なる例外が出るという仕様ではありません。公式は同時利用時の動作を未定義としているため、種類や順序に依存せず、原則として検出を既定のままにしてください。

## 4. 参考ドキュメント

- [パフォーマンスの概要 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/performance/)
- [効率的なクエリ | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/performance/efficient-querying)
- [高度なパフォーマンストピック | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/performance/advanced-performance-topics)
- [NativeAOT のサポートと事前コンパイル済みクエリ | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/performance/nativeaot-and-precompiled-queries)
- [EF Core の MSBuild 統合 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/cli/msbuild)
- [クエリタグ | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/querying/tags)
- [Microsoft.Extensions.Logging による EF Core のログ | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/logging-events-diagnostics/extensions-logging)
- [簡易ログ | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/logging-events-diagnostics/simple-logging)
- [RelationalQueryableExtensions.CreateDbCommand メソッド | Microsoft Learn](https://learn.microsoft.com/ja-jp/dotnet/api/microsoft.entityframeworkcore.relationalqueryableextensions.createdbcommand?view=efcore-10.0)
- [EF Core のメトリック | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/logging-events-diagnostics/metrics)
- [インデックス | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/modeling/indexes)
- [SQL Server プロバイダーのインデックス | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/sql-server/indexes)
- [オンラインインデックス操作のガイドライン | Microsoft Learn](https://learn.microsoft.com/ja-jp/sql/relational-databases/indexes/guidelines-for-online-index-operations?view=sql-server-ver16)
- [オンラインでのインデックス操作 | Microsoft Learn](https://learn.microsoft.com/ja-jp/sql/relational-databases/indexes/perform-index-operations-online?view=sql-server-ver16)
- [SortInTempDb メソッド | Microsoft Learn](https://learn.microsoft.com/ja-jp/dotnet/api/microsoft.entityframeworkcore.sqlserverindexbuilderextensions.sortintempdb?view=efcore-10.0)
- [UseDataCompression メソッド | Microsoft Learn](https://learn.microsoft.com/ja-jp/dotnet/api/microsoft.entityframeworkcore.sqlserverindexbuilderextensions.usedatacompression?view=efcore-10.0)
- [DataCompressionType 列挙型 | Microsoft Learn](https://learn.microsoft.com/ja-jp/dotnet/api/microsoft.entityframeworkcore.datacompressiontype?view=efcore-10.0)
- [Azure Cosmos DB プロバイダーの制限事項 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/cosmos/limitations)
- [Azure Cosmos DB プロバイダー | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/cosmos/)
- [AddCosmos メソッド | Microsoft Learn](https://learn.microsoft.com/ja-jp/dotnet/api/microsoft.extensions.dependencyinjection.cosmosservicecollectionextensions.addcosmos?view=efcore-10.0)
- [Cosmos SDK の HttpClientFactory | Microsoft Learn](https://learn.microsoft.com/ja-jp/dotnet/api/microsoft.azure.cosmos.cosmosclientoptions.httpclientfactory?view=azure-dotnet)
- [Cosmos SDK の IdleTcpConnectionTimeout | Microsoft Learn](https://learn.microsoft.com/ja-jp/dotnet/api/microsoft.azure.cosmos.cosmosclientoptions.idletcpconnectiontimeout?view=azure-dotnet)
- [Cosmos DB の有効期限 | Microsoft Learn](https://learn.microsoft.com/ja-jp/azure/cosmos-db/time-to-live)
- [Cosmos DB プロバイダーの非構造化データ | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/cosmos/unstructured-data)
- [ID 解決 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/change-tracking/identity-resolution)
- [Azure Cosmos DB プロバイダーでのモデリング | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/cosmos/modeling)
- [Azure Cosmos DB プロバイダーでのクエリ | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/cosmos/querying)
- [Azure Cosmos DB プロバイダーの全文検索 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/cosmos/full-text-search)
- [Azure Cosmos DB プロバイダーのベクトル検索 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/cosmos/vector-search)
- [Azure Cosmos DB の全文検索 | Microsoft Learn](https://learn.microsoft.com/ja-jp/azure/cosmos-db/gen-ai/full-text-search)
- [Azure Cosmos DB のベクトル検索 | Microsoft Learn](https://learn.microsoft.com/ja-jp/azure/cosmos-db/vector-search)
- [FullTextScore | Microsoft Learn](https://learn.microsoft.com/ja-jp/cosmos-db/query/fulltextscore)
- [HasDefaultFullTextLanguage メソッド | Microsoft Learn](https://learn.microsoft.com/ja-jp/dotnet/api/microsoft.entityframeworkcore.cosmosmodelbuilderextensions.hasdefaultfulltextlanguage?view=efcore-10.0)
- [Rrf メソッド | Microsoft Learn](https://learn.microsoft.com/ja-jp/dotnet/api/microsoft.entityframeworkcore.cosmosdbfunctionsextensions.rrf?view=efcore-10.0)
- [データベースプロバイダー | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/)
- [EF Core の NuGet パッケージ | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/nuget-packages)
- [EF Core 9.0 の破壊的変更 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/breaking-changes)
- [付加列を使用したインデックスの作成 | Microsoft Learn](https://learn.microsoft.com/ja-jp/sql/relational-databases/indexes/create-indexes-with-included-columns?view=sql-server-ver17)

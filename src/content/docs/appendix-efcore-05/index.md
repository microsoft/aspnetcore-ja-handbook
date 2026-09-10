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

SQLite で同じ `CountAsync()` クエリを実行し、書式オプションを省略した場合と比べました（EF Core 10.0.11 で実測）。

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
> `SingleLine` は改行を**取り除くだけ**で、空白に置き換えるわけではありません。実測のとおり `...CommandTimeout='30']SELECT` のように語がつながります。構造化ログへ変換する機能ではないため、出力先でこの形式を正しく扱えることを確認して使ってください。

> [!TIP]
> `LogTo` の書式オプションを省略した場合、既定は `DefaultWithLocalTime` で、現在のカルチャーに従ったローカル時刻が出力されます。[公式のログ書式の説明](https://learn.microsoft.com/ja-jp/ef/core/logging-events-diagnostics/simple-logging#message-contents-and-formatting)も同じです。ほかのメタデータを維持して UTC 時刻へ変えるには `DefaultWithUtcTime` を指定します。

### EF Core が公開しているメトリックを見る

ログよりも軽量に、アプリケーション全体の傾向をつかみたいときはメトリックが使えます。EF Core 9 以降、EF Core は `System.Diagnostics.Metrics` API で **`Microsoft.EntityFrameworkCore`** という名前のメーターを公開しています。公式ドキュメントに記載されているのは次の 7 つです。

| メトリック | 種類 | 意味 |
| --- | --- | --- |
| `microsoft.entityframeworkcore.active_dbcontexts` | UpDownCounter | 現在アクティブな `DbContext` の数 |
| `microsoft.entityframeworkcore.queries` | Counter | 実行されたクエリの累計 |
| `microsoft.entityframeworkcore.savechanges` | Counter | `SaveChanges` の累計 |
| `microsoft.entityframeworkcore.compiled_query_cache_hits` | Counter | クエリキャッシュにヒットした回数 |
| `microsoft.entityframeworkcore.compiled_query_cache_misses` | Counter | クエリキャッシュを外した回数 |
| `microsoft.entityframeworkcore.execution_strategy_operation_failures` | Counter | 実行戦略が捉えた操作の失敗回数 |
| `microsoft.entityframeworkcore.optimistic_concurrency_failures` | Counter | 楽観的同時実行制御の失敗回数 |

`MeterListener` で購読して実測してみます。**同じ形のクエリを 3 回、別の形のクエリを 1 回、`SaveChanges` を 1 回**実行した結果です。

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
> 1 つ目は、EF Core のメトリックがすべて**観測可能な計測器 (observable instrument)** であることです。値を取り出すには `RecordObservableInstruments()` を明示的に呼ぶ必要があります。これを忘れるとコールバックが 1 度も呼ばれません（実測で確認）。
>
> 2 つ目は、**計測器によって値の型が違う**ことです。EF Core のソースコードを見ると、`active_dbcontexts` だけが `ObservableUpDownCounter<int>` で、残りの 6 つは `ObservableCounter<long>` として作られています。`SetMeasurementEventCallback<long>` だけを登録すると、**`active_dbcontexts` の行だけが黙って出てきません。** 上のコードで `<int>` 版も登録しているのはこのためです（実測で確認）。

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

同じ処理を 50 回ずつ繰り返してヒット率を実測すると、次のようになりました。

| 書き方 | ヒット率 |
| --- | --- |
| 同じ形の LINQ クエリを繰り返す | 98% |
| `EF.Constant()` で値をインライン化する | 98% |
| 条件式を実行時に付け外しして形を変える | 92% |
| `FromSqlRaw` に、値を直接埋め込んだ毎回異なる SQL を渡す | **0%** |

> [!NOTE]
> **この測定では、同じ式の形で `EF.Constant()` に渡す値を変えても、50 回中 49 ヒット・1 ミスでした。** SQL に値を埋め込むことと、EF Core のクエリキャッシュでミスが起きることは同義ではありません。ただし式の組み立て方や形が変わる場合までヒット率が不変だとはいえません。値を埋め込んだ SQL がデータベース側のプランキャッシュへ与える影響は、EF Core のメトリックとは別に確認します。
>
> ただしこれは EF Core 9 以降の挙動です。EF Core 8 の実装では `EF.Constant()` がクエリキャッシュより前の段階で定数ノードを埋め込んでいたため、値が変わるたびに EF Core 側でもキャッシュミスが発生していました。EF Core 9 でこの処理はパイプラインの後段へ移されています。
>
> 表の生 SQL の実測では、各回で SQL 文字列そのものを変えています。**文字列連結を使うだけで、必ずヒット率が 0% になるわけではありません。** EF Core 10.0.11 と SQLite で 50 回ずつ対照実測すると、同一の SQL 文字列を繰り返した場合とパラメーター化した場合のクエリコンパイルはそれぞれ 1 回、毎回異なる SQL では 50 回でした。[EF Core 10.0.11 の公式実装](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore.Relational/Query/Internal/FromSqlQueryRootExpression.cs)でも、生 SQL のクエリ式の等価比較に SQL 文字列を含めることが確認できます。
>
> 同期間のヒット・ミスから求めた比率が低いなら、まず生 SQL の組み立て方と、条件を動的に付け外ししている箇所を疑ってください。パラメーター化を保ったまま生の SQL を書く方法は[付録3の「生の SQL を使う」](../appendix-efcore-03/index.md#生の-sql-を使う)を参照してください。

`active_dbcontexts` が想定より多いままなら `DbContext` が破棄されずに残っている可能性があり、`optimistic_concurrency_failures` の増加は同時更新の競合が実際に起きていることを示します。これらは OpenTelemetry や Application Insights にそのまま送れます。

> [!TIP]
> **`active_dbcontexts` は「正常な状態」を先に知っておくと役に立ちます。** `AddDbContextPool` を使い、スコープごとに `DbContext` を取得してクエリを 1 回発行する処理を 10 分間で 2,800 回繰り返しながら、定期的に値を記録したところ、次のようになりました。
>
> | 経過 | 反復回数 | マネージドヒープ | ワーキングセット | `active_dbcontexts` |
> | --- | --- | --- | --- | --- |
> | 42 秒 | 200 | 8.6 MiB | 139.4 MiB | 1 |
> | 292 秒 | 1,400 | 9.6 MiB | 141.1 MiB | 1 |
> | 600 秒 | 2,800 | 8.9 MiB | 141.2 MiB | 1 |
>
> マネージドヒープには 4 MiB 台まで戻る変動があり、ワーキングセットの増加は最初の記録（42 秒）から最後の記録（600 秒）までで約 1.8 MiB でした。ここで 1 MiB は 1,048,576 バイトです。`active_dbcontexts` は**各記録時点で 1**でした。スコープの終了時に確実にプールへ返却されているためです。
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

どちらもどの程度なのかを実測しました。まず出力量です。SQL Server 2022 に対してウォームアップのクエリ 1 回と主キー検索 2,000 回を実行し、`LogTo` の出力をファイルに書き出しました。

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

ログなしの総時間をクエリ数で割ると、SQL Server は約 0.7 ミリ秒、SQLite は約 0.05 ミリ秒です。SQLite のこの測定ではログありの時間が約 33% 長くなりましたが、SQL Server 側の差が小さい理由や統計的な有意差まではこの結果だけで判断できません。

> [!NOTE]
> この 2 条件ではログによる時間差が異なりましたが、データベースの速さだけを原因とは断定できません。SQL Server 側は `StreamWriter`（計時後に `Flush`）、SQLite 側はメモリ上の `StringWriter` への出力で、後者はディスク書き込みを測っていません。ファイルへ保存する場合の容量・書き込み時間も、実際の出力先で別途確認してください。本番の詳細ログは必要な期間に限定します。

---

### ログとセキュリティ

EF Core は、既定ではパラメーター値をログに出力しません。パラメーター名やサイズなどのメタデータは残りますが、値は `?` に置き換えられます。実際に SQL Server 2022 に対して `city` というローカル変数で絞り込んだところ、次のように出力されました（実測で確認）。

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
> なお、この簡素化が適用されるのは LINQ クエリのパラメーターです。`SaveChanges` が発行する `INSERT` / `UPDATE` は実測でも従来どおり `@p0`、`@p1` という名前でした。

ただし EF Core は、状況によってはパラメーターを送らずに値を SQL に **インライン化** することがあります。`EF.Constant()` を明示的に使った場合が代表例です。EF Core 9 まではインライン化された値がログにそのまま出力されていましたが、EF Core 10 以降は既定で `?` に置き換えられるようになりました。次は SQL Server 形式で示した模式例です。直接リテラル・`EF.Constant()`・通常のパラメーターの対照実測は EF Core 10.0.11 / SQLite で行いました。

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

EF Core 10 からは、接続文字列に `Application Name` が指定されていない場合、EF Core が自身のバージョンと OS 情報を含む値を **自動的に追加** します。実際に SQL Server プロバイダーで確認すると、次のように書き換えられます。

```text
[渡した接続文字列]
Server=localhost;Database=Test;Trusted_Connection=True;TrustServerCertificate=True

[EF Core が使う文字列]
Data Source=localhost;Initial Catalog=Test;Integrated Security=True;
Trust Server Certificate=True;Application Name="EFCore/10.0.11 (macOS 26.6.2 Arm64)"
```

ほとんどの場合は影響しませんが、**同じデータベースに EF Core と Dapper や ADO.NET などを併用している場合は注意が必要です。** SqlClient は接続文字列が異なると別の接続プールを使うため、両者が別々のプールに分かれます。同一の `TransactionScope` 内で別プールの接続を使うと、**分散トランザクションへの昇格** が必要になる可能性があります。以下は接続を 2 本開いた条件での観測です。

> [!WARNING]
> **.NET 7 以降、暗黙の昇格は既定で無効化されています。** そのため実際に起きるのは「昇格して動き続ける」ことではなく、昇格が必要になった操作で例外が発生することです。Windows Server 2022 + SQL Server 2022 + .NET 10 で `TransactionScope` の中から接続文字列の違う接続を 2 本開いたところ、2 本目の `Open()` で次の例外が発生しました。
>
> ```text
> System.NotSupportedException: Implicit distributed transactions have not been enabled.
> If you're intentionally starting a distributed transaction, set
> TransactionManager.ImplicitDistributedTransactions to true.
> ```
>
> `TransactionManager.ImplicitDistributedTransactions = true`（Windows 専用の API です）を設定したうえで同じコードを実行すると、2 本目の `Open()` の直後に `Transaction.Current.TransactionInformation.DistributedIdentifier` が `Guid.Empty` から実際の GUID に変わり、MSDTC (Microsoft Distributed Transaction Coordinator) への昇格が起きたことを確認できました。Linux や macOS では、このプロパティを `true` に設定する時点で `PlatformNotSupportedException` になります。macOS / .NET 10 で設定時の例外を確認しました。

同じ環境で条件を変えて測ったところ、昇格するかどうかは次のように分かれました。

| `TransactionScope` 内での操作 | 昇格するか |
| --- | --- |
| 同じ接続文字列の接続を 2 本 **同時に** 開く | 昇格する |
| 同じ接続文字列で、1 本目を閉じてから 2 本目を開く | **昇格しない** |
| `Application Name` だけが違う接続を 2 本同時に開く | 昇格する |
| `Application Name` だけが違う接続を、1 本目を閉じてから 2 本目を開く | **昇格する** |

つまり、接続を律儀に閉じてから次を開く書き方をしていれば以前は昇格しなかったのに、EF Core 10 が `Application Name` を自動で足したことで昇格するようになる、というのがこの破壊的変更の実害です。

回避するには、接続文字列に `Application Name` を明示的に指定します。空でなく、ドライバーの既定名とも異なるアプリケーション固有の値を指定すると、EF Core はその名前を維持します。空文字列やドライバーの既定名は自動設定の対象なので、「何か指定すれば必ず維持される」とは考えないでください。SQL Server 2022 に接続して `sys.dm_exec_sessions` の `program_name` を確認したところ、指定しない場合は `EFCore/10.0.11 (macOS 26.6.2 Arm64)`、明示した場合は `BloggingApi` となり、サーバー側から見える値も切り替わることを確認しています。

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

実際に接続文字列を変えて 2 回登録し、解決された `DbContext` の接続先を確認したところ、2 つ目の設定が使われました（実測）。公式ドキュメントは、競合しない構成を合成できる `ConfigureDbContext` との一貫性のための変更と説明しています。EF Core 10.0.11 と SQLite の対照実測でも、接続先は後の値へ変わりましたが、最初に指定した `NoTracking` は維持されました。**`DbContext` 本体の登録は `TryAdd` のままです。** [EF Core 10.0.11 の実装](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Extensions/EntityFrameworkServiceCollectionExtensions.cs#L597)では、コンテキスト本体の登録と構成処理の追加を分け、オプションを作るときに登録済みの構成を順に適用します。コンテキスト本体の DI 登録と、設定の合成・競合解決を混同しないでください。

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

厄介なのは、**壊れ方が登録順に依存する**ことです。2 つの `DbContext` を登録して実測すると、次のようになりました。

| 登録順 | `GoodContext` の解決 | `BadContext` の解決 |
| --- | --- | --- |
| `GoodContext` → `BadContext` | 成功 | **たまたま成功** |
| `BadContext` → `GoodContext` | 成功 | **失敗** |

非ジェネリック版は「最後に登録された `DbContextOptions`」を拾うため、たまたま自分の登録が最後だったときだけ動いてしまいます。失敗する側では次の例外が出ました。

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

その用途には、EF Core 9 以降の **`ConfigureDbContext`** を使えます。[公式リファレンス](https://learn.microsoft.com/ja-jp/dotnet/api/microsoft.extensions.dependencyinjection.entityframeworkservicecollectionextensions.configuredbcontext?view=efcore-10.0)は、構成が呼び出し順に適用され、競合する設定は後の構成で上書きされると説明しています。**`AddDbContext` を再度呼ぶと、以前の構成全体が消えるわけではありません。** EF Core 10 と SQLite で、最初の `AddDbContext` で接続を指定し、2 回目でログと機密データの記録だけを追加すると、同じ接続でクエリを実行でき、追加したログ設定も有効でした。

`ConfigureDbContext` 自体はコンテキストを DI に登録しないため、`AddDbContext` などとの併用が必要です。実測でも、`ConfigureDbContext` だけではコンテキストを解決できませんでした。次の例は、共通側の診断設定とアプリケーション側の登録を分けたものです。

```csharp
// ライブラリやテスト側：診断の設定だけを足す
services.ConfigureDbContext<BloggingContext>(options =>
    options.LogTo(Console.WriteLine)
           .EnableSensitiveDataLogging());

// アプリケーション側：プロバイダーと接続文字列を決める
services.AddDbContext<BloggingContext>(options =>
    options.UseSqlServer(connectionString));
```

実際にこの順番で登録して解決した `DbContext` でクエリを実行したところ、プロバイダーは SQL Server のまま、`LogTo` も `EnableSensitiveDataLogging` も有効でした（実測）。

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

1 つの `DbContext` インスタンスに対して 2 つの操作を同時に実行すると、実際に次の例外になります（実測）。

```text
System.InvalidOperationException: A second operation was started on this context
instance before a previous operation completed. This is usually caused by
different threads concurrently using the same instance of DbContext.
```

対して、`IDbContextFactory<T>` から操作ごとにインスタンスを作れば、同じ 2 つの操作を並行実行しても例外は発生しませんでした。

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

EF Core は接続プーリングを自前では実装せず、下位のドライバーに任せます。そして **EF Core は操作の直前に接続を開き、直後に閉じてプールへ返します。** 必要以上に接続をプールの外に出しておかないためです。`IDbConnectionInterceptor` で開閉の回数を数えたところ、同じ `DbContext` インスタンスでクエリを 3 回実行すると 3 回開閉していました。

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
> DI のスコープ検証では、もう 1 つ「**Scoped サービスをルートのサービスプロバイダーから解決していないか**」も確認されます。`app.Services.GetRequiredService<BloggingContext>()` のようにスコープを作らずに解決すると、次の例外になります（実測）。
>
> ```text
> System.InvalidOperationException: Cannot resolve scoped service
> 'BloggingContext' from root provider.
> ```
>
> ルートコンテナーで作られた Scoped サービスは、アプリケーションの終了時まで破棄されず、実質的に Singleton に昇格してしまうためです。バックグラウンドサービスからは `IServiceScopeFactory` でスコープを作るか、`IDbContextFactory<T>` を使ってください。

#### サードパーティー製プロバイダーはバージョンを実際に確かめる

EF Core のプロバイダーは **通常、異なるメジャーバージョンとの互換性はありません**。公式のプロバイダー一覧には各プロバイダーが対応する EF Core のバージョンが載っていますが、この一覧は Microsoft 以外が提供するプロバイダーの最新状況に追いついていないことがあります。次の指定バージョンを復元し、限定した操作を確かめました。復元成功は、そのパッケージの全機能の互換性を保証するものではありません。

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

NuGet は推移的な依存関係について「条件を満たす最も低いバージョン」を選ぶため、プロバイダーが古いパッチ版を指していると、そのまま古い `Relational` が使われます。実測すると次のようになりました。

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

**Microsoft が提供する `Microsoft.EntityFrameworkCore.*` パッケージは同じバージョンに揃えてください。** これは公式の NuGet パッケージ案内に明記されています。`SqlServer` 自体を 10.0.11 に更新して復元すると、直接参照していない `Relational` も 10.0.11 になりました。`Relational` だけの更新を、プロバイダー自身の修正まで取り込む方法と取り違えないでください。

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

ここで注意したいのは、**ズレは「一覧が新しすぎる」方向だけでなく「一覧が古すぎる」方向にも起きる**ことです。EF Core 10 のプロジェクトへ次の指定バージョンを復元し、2 つの拡張の DDL 生成を確認しました。

| パッケージ | 公式一覧の記載 | 実際に復元されたバージョン |
| --- | --- | --- |
| `EFCore.CheckConstraints` | 5-9 | **10.0.0** |
| `EFCore.NamingConventions` | 3-9 | **10.0.1** |
| `EFCore.BulkExtensions` | 2-8 | **10.0.1** |

指定した 3 パッケージの復元は成功しました。ただし、これだけで全機能の対応を保証することはできません。実際に `UseSnakeCaseNamingConvention()` と `UseEnumCheckConstraints()` を併用して `GenerateCreateScript()` を実行したところ、期待どおりの DDL が生成されました（実測）。

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

**一覧に「未対応」と書かれていても、それだけで諦めないでください。** 逆に一覧に載っていても、実際に復元して動かすまでは対応済みとみなさないでください。判断には公式一覧、依存バージョン、アプリケーションで必要な操作の実行結果を組み合わせてください。

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
> ここで示した既定の `AddDbContextFactory` 登録では、`IDbContextFactory<BloggingContext>` を **Singleton** として登録すると同時に、`BloggingContext` そのものも **Scoped** で登録します（実測で確認）。したがって、コントローラーで `BloggingContext` を直接受け取ることも、バックグラウンドサービスでファクトリからインスタンスを作ることも、両方できます。ただし **ファクトリで作ったインスタンスは DI コンテナーが破棄してくれない**ため、下の例のように `await using` で必ず自分で破棄してください。

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

EF Core 10.0.11 / SQLite を構成した最小 Context で、ハッシュ値ではなく参照の同一性を比較しました。`AddDbContextFactory` では 4 回続けて `CreateDbContextAsync()` を呼ぶと 4 つの別インスタンスが返りましたが、`AddPooledDbContextFactory` では**同じインスタンスが 4 回とも返りました**（破棄するたびにプールへ戻り、次の要求で再利用されるため）。また破棄前に 1 件のエンティティを追跡させておいても、プールから取り出し直したインスタンスの追跡数は 0 に戻っていました。

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

SQL Server 2022 の 20,000 行のテーブルで統計を更新し、手書きの `SELECT Id` に対して `SET SHOWPLAN_ALL ON` で推定プランの物理演算子を確認しました。次の LINQ 欄は条件を対応づけるための表記で、掲載した LINQ が生成する全 SQL をそのまま実行した結果ではありません。

| 条件に対応する書き方 | 手書き SQL で調べた条件 | 推定プランの物理演算子 |
| --- | --- | --- |
| `Title.StartsWith("T0001")` | `LIKE N'T0001%'` | **Index Seek** |
| `Title.EndsWith("A")` | `LIKE N'%A'` | Index Scan |
| 複合インデックス `(BlogId, Price)` を `BlogId` で絞る | `BlogId = 7` | **Index Seek** |
| 同じインデックスを `Price` **だけ**で絞る | `Price = 7` | Index Scan |
| 同じインデックスを両方で絞る | `BlogId = 7 AND Price = 7` | **Index Seek** |
| 列に対する式で絞る | `Price / 2 = 7` | Index Scan |

ここから読み取れることは 2 つあります。

1. **複合インデックスでは先頭列が重要です。** この測定では `(BlogId, Price)` の先頭列 `BlogId` を含む条件でシーク、`Price` だけではスキャンになりました。ただし `Index Scan` もインデックスを読み取る演算子であり、「インデックスがまったく使われていない」という意味ではありません。`Price` 単独の検索を効率化したい場合は、列順序や別インデックスを実際のプランで検討します。
2. **列に式を適用すると、通常の列インデックスで条件をシークできない場合があります。** この測定の `Price / 2 = 7` はスキャンでした。公式は、永続化された計算列にインデックスを作る方法や、対応するデータベースで式インデックスを使う方法を挙げています。

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

別データベースの計算列用テーブル `Post2` に 20,000 行を用意し、`HalfPrice` のインデックスに対して `WHERE [HalfPrice] = 7` を実行すると、物理演算子は **Index Seek** に変わりました。計算列については「[計算列](../appendix-efcore-02/index.md#計算列)」も参照してください。

#### インデックスに列を含める（付加列）

絞り込みには使わないが取得はする列を、インデックスの**キーではない列**としてインデックスに持たせられます。公式ドキュメントは「クエリで使うすべての列がキー列か非キー列としてインデックスに含まれていれば、テーブル自体にアクセスする必要がなくなるため、クエリのパフォーマンスが大きく向上する可能性がある」と説明しています。

次の構成は、直前の `IndexContext.OnModelCreating` の末尾へ追加します。

```csharp
modelBuilder.Entity<Post>()
    .HasIndex(p => p.Price)
    .IncludeProperties(p => p.Title);
```

SQL Server 2022 に対してこのモデルで `GenerateCreateScript()` を実行すると、次の DDL が生成されました。

```sql
CREATE INDEX [IX_Posts_Price] ON [Posts] ([Price]) INCLUDE ([Title]);
```

効果を確かめるため、20,000 行の `Posts` テーブルに対して `Price` で絞り込み `Title` と `Price` を取得するクエリを、`INCLUDE` の有無だけを変えて `SET SHOWPLAN_ALL ON` で比較しました。

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

`INCLUDE` がない場合、SQL Server はインデックスで該当行を見つけたあと、`Title` を取りに行くためにクラスター化インデックスを引き直しています（キー参照）。`Title` をインデックスに含めるとこの往復がなくなり、インデックスだけでクエリが完結しました。

> [!NOTE]
> 付加列はインデックスのサイズや更新コストに影響します。追試では付加列の追加後にキー検索が不要になり、結果も一致しましたが、所要時間や I/O の改善率は測っていません。「絞り込みには使わないが、そのクエリで必ず一緒に取得する列」に絞って指定してください。

#### SQL Server 固有のインデックス構成

インデックスの細かな性質はデータベースごとに異なるため、EF Core はプロバイダー固有の API で構成します。SQL Server プロバイダーでは**クラスター化**と**フィル ファクター**を指定できます。

```csharp
modelBuilder.Entity<Blog>().HasIndex(b => b.PublishedOn).IsClustered();

modelBuilder.Entity<Blog>().HasIndex(b => b.PublishedOn).HasFillFactor(80);
```

`IsClustered(false)` と `HasFillFactor(80)` を指定したモデルで `GenerateCreateScript()` を実行すると、次の DDL が生成されました。

```sql
CREATE NONCLUSTERED INDEX [IX_Posts_Price] ON [Posts] ([Price]) WITH (FILLFACTOR = 80);
```

> [!NOTE]
> クラスター化インデックスはテーブルごとに 1 つだけです。EF Core は主キーに対してクラスター化インデックスを既定で作成するため、別の列を `IsClustered()` にする場合は主キー側を `IsClustered(false)` にする必要があります。

オンラインで作成する場合は、`HasIndex(...).IsCreatedOnline()` を指定します。SQL Server 2022 Developer Edition で実測したモデルからは、次の DDL が生成され、実際の作成も成功しました。

```sql
-- SQL Server
CREATE INDEX [IX_OnlineItems_Name]
ON [OnlineItems] ([Name]) WITH (ONLINE = ON);
```

> [!WARNING]
> **`ONLINE = ON` は、ロックも待機も発生しないという意味ではありません。** 別トランザクションにスキーマロックを保持させた実測では、オンライン作成も待機し、ロックタイムアウトで SQL エラー 1222 になりました。ロックを解放すると同じ DDL が成功しました。公式も、オンライン操作の終盤には共有ロックやスキーマ変更ロックを保持すると説明しています。利用可否は SQL Server のエディションにも依存するため、Developer Edition の結果をすべての運用環境へ当てはめないでください。

同じインデックス設定に `SortInTempDb()` と `UseDataCompression(DataCompressionType.Page)` を追加すると、DDL は次のようになります。この組み合わせも SQL Server 2022 Developer Edition で作成と読み書きを実測しました。

```sql
-- SQL Server
CREATE INDEX [IX_OnlineItems_Name] ON [OnlineItems] ([Name])
WITH (ONLINE = ON, SORT_IN_TEMPDB = ON, DATA_COMPRESSION = PAGE);
```

`DataCompressionType.None` / `Row` / `Page` の 3 条件で、`sys.partitions.data_compression_desc` はそれぞれ `NONE` / `ROW` / `PAGE` になりました。これは**設定が反映されたことの確認**であり、圧縮率や tempdb 使用量、性能改善を測定したものではありません。

### 実行プランはデータの量で変わる

公式ドキュメントは、実行プランを分析するときの前提として「**データベースは、実際に入っているデータに応じて異なるクエリプランを生成することがある**。たとえばテーブルに数行しかなければ、インデックスを使わずにテーブル全体をスキャンすることを選ぶかもしれない。**テスト用データベースでプランを分析するなら、本番と似たデータが入っていることを必ず確認すること**」と述べています。ベンチマークについても同じ注意が繰り返されています。

追加追試では SQL Server 2022 CU26 で同じクエリとインデックスを使い、5 行と 100,005 行で統計を更新してプランを比べました。`Filler` は `varchar(8000)` に 8,000 文字、`Rating` は 1〜5 の繰り返しとし、両条件で `UPDATE STATISTICS ... WITH FULLSCAN` を実行しました。列の幅や値の分布もプラン選択に関わるため、行数だけで一般化しないでください。これは推定プランの対照で、経過時間を比較したものではありません。

```sql
-- SQL Server（Rating には非クラスター化インデックスがある）
SELECT Id, Rating, Filler FROM Blogs WHERE Rating = 3;
```

| テーブルの状態 | 選ばれた物理演算子 |
| --- | --- |
| 5 行 | `Clustered Index Scan` |
| 100,005 行（`Rating = 3` は 20,001 件） | `Index Seek` + `Key Lookup` |

この追加追試では、少量側がスキャン、多量側がシークとキー検索になりました。20% という一致率だけでプランを予測することはできません。また `Clustered Index Scan` はクラスター化インデックスを読み取る演算子で、インデックス不使用を意味しません。

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

個別パラメーター方式はコレクションの件数をプラン選択に利用できる一方、異なる SQL が増える可能性もあります。ただし EF Core 10 はパラメーター数をパディングするため、要素数が変わるたびに SQL の形が変わるわけではありません。EF Core 10.0.11 の生成確認では、6・7・8・9 要素はいずれも 10 パラメーターで同じ SQL になりました。どの方式が有利かは対象データベースのプランと実行結果で比較します。

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
> EF Core 10.0.11 でクエリ単位に使える指定は `EF.Parameter` と `EF.Constant` で、公開 API に `EF.MultipleParameters` はありません。個別パラメーター方式を基本にするなら全体設定を `MultipleParameters` のままにし、一部のクエリだけ `EF.Parameter` または `EF.Constant` で変更すると、Context の設定をクエリごとに切り替えずに済みます。

| `ParameterTranslationMode` | 動作 |
| --- | --- |
| `MultipleParameters` | 要素ごとのパラメーター。EF Core 10 の既定 |
| `Parameter` | JSON 配列パラメーター 1 つ。EF Core 8・9 の既定 |
| `Constant` | 値を SQL に直接埋め込む。EF Core 7 までの既定 |

> [!TIP]
> 定数方式では、値や要素数の違いによって異なる SQL が生成され、データベース側のプランキャッシュへ影響する可能性があります。EF Core 側のクエリキャッシュとは別に確認してください。掲載した 50 回の測定では、同じ式の形を保った通常のパラメーター化と `EF.Constant()` はどちらも 49 ヒット・1 ミス（98%）でしたが、あらゆる式の組み立て方でヒット率が不変という保証ではありません。

### コンパイル済みクエリ

EF Core は同じ形のクエリに対して内部でクエリプランをキャッシュしますが、LINQ 式ツリーの走査とキャッシュキーの計算コストは毎回発生します。ホットパスのクエリでは **コンパイル済みクエリ** によりこのコストを削減できます。ただし削減されるのはこの前処理だけであり、効果は控えめです。SQLite に対する 1 件検索を 3,000 回繰り返して測ると、0.162 ミリ秒が 0.124 ミリ秒（約 1.3 倍速）になる程度でした。同じクエリを高頻度で再利用し、この前処理が実際にボトルネックになっている場合に検討してください。毎秒の実行回数だけを適用の境界とは考えないでください。

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
> 2 つ目の制限は **ラムダの中に書くパラメーター式の形** に対するものであり、「パラメーターの型がコレクションであってはいけない」という意味ではありません。実際に `EF.CompileAsyncQuery((BloggingContext context, int[] ids) => context.Blogs.Where(b => ids.Contains(b.Id)))` を定義して `new[] { 1, 3 }` を渡したところ、SQL Server 2022・SQLite のどちらでも該当する 2 件が正しく返りました（EF Core 10.0.11、実測で確認）。一方、`(BloggingContext context, Filter f) => context.Blogs.Where(b => b.Name == f.Name)` のようにパラメーターのメンバーへアクセスする式を書くと、実行時に `InvalidOperationException`（`The LINQ expression ... could not be translated.`）になります。値は変数としてそのまま渡し、ラムダの中に `obj.Property` や `obj.GetValue()` のような式を書かない、と理解してください。

#### コンパイル済みクエリの中では `EF.Constant` と `EF.Parameter` が使えない

[コレクションのパラメーター化と IN 句の翻訳](#コレクションのパラメーター化と-in-句の翻訳)で紹介した `EF.Constant` と `EF.Parameter` は、**コンパイル済みクエリの中では使えません。** EF Core 9 の破壊的変更です。

```csharp
var query = EF.CompileAsyncQuery(
    (AppDbContext context, int[] ids) => context.Customers.Where(c => EF.Constant(ids).Contains(c.Id)));
```

```text
InvalidOperationException: The 'EF.Constant<T>' method may only be used with an argument
that can be evaluated client-side and does not contain any reference to database-side entities.
```

`EF.Parameter` でも同じ例外になることを確認しました。コンパイル済みクエリはクエリの形を一度だけ確定させる仕組みなので、呼び出しごとに SQL の形が変わりうるこれらの指定とは両立しません。片方を諦める必要があります。

#### 値変換に使うメソッドを private にしてはいけない

コンパイル済みモデルには、値変換 (value converter) と組み合わせたときの落とし穴があります。EF Core 9 以降、生成されるコードは**変換メソッドそのものを直接参照します**。そのメソッドが `private` だと、生成されたコードがコンパイルできません。

```csharp
public sealed class BooleanToCharConverter()
    : ValueConverter<bool, char>(v => ConvertToChar(v), v => ConvertToBoolean(v))
{
    public static readonly BooleanToCharConverter Default = new();

    private static char ConvertToChar(bool value) => value ? 'Y' : 'N';    // private だと失敗する
    private static bool ConvertToBoolean(char value) => value == 'Y';
}
```

このモデルで `dotnet ef dbcontext optimize` を実行すると、コマンド自体は成功します。**失敗するのは、その後のビルドです**（実測）。

```text
CompiledModels/FlaggedEntityType.cs(64,124): error CS0122:
'BooleanToCharConverter.ConvertToChar(bool)' はアクセスできない保護レベルになっています
```

生成されたコードを覗くと、確かにメソッドが直接呼ばれています。

```csharp
string (bool v) => string.Format(CultureInfo.InvariantCulture, "{0}",
    ((object)(BooleanToCharConverter.ConvertToChar(v)))),
```

公式ドキュメントによれば、これは **NativeAOT に対応するために必要だった変更**です。対処は単純で、変換メソッドを `public` か `internal` にします。実測でも `internal` に変えるだけでビルドが通りました。

> [!WARNING]
> この例ではコンパイル済みモデルの生成自体は成功し、生成コードを含めたプロジェクトのビルドで `private` メソッドへのアクセスが失敗しました。一方、通常モデルを使う SQL Server プロバイダーの SQL 生成は、同メソッドが `private` のままで成功しました。この対照は SQL 生成とビルドの確認であり、通常モデルでの保存・再読み込みを測ったものではありません。

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
> コンパイル済みモデルにはいくつかの制限があります。グローバルクエリフィルター、遅延読み込みプロキシ、変更追跡プロキシ、カスタムの `IModelCacheKeyFactory` はサポートされません。また、モデルを変更するたびに再生成が必要で、再生成を忘れると実行時に古いモデルが使われます。エンティティが数十個程度のアプリケーションでは効果が小さいため、起動時間が実測で問題になっている場合にのみ検討してください。
>
> グローバルクエリフィルターを設定したモデルに対して `dotnet ef dbcontext optimize` を実行すると、実際に次のエラーで失敗することを確認しています。
>
> ```text
> System.InvalidOperationException: The entity type 'Blog' has a query filter configured.
> Compiled model can't be generated, because query filters are not supported.
> ```
>
> つまり「生成はできたが一部の機能が無視される」のではなく、**生成そのものが失敗します**。[グローバルクエリフィルターと名前付きクエリフィルター](../appendix-efcore-02/index.md#グローバルクエリフィルターと名前付きクエリフィルター)を使っている場合は、コンパイル済みモデルを併用できない点に注意してください。

### NativeAOT と事前コンパイル済みクエリ

.NET の **NativeAOT** は、アプリケーションを事前 (ahead-of-time) にネイティブコードへコンパイルして発行する仕組みです。起動が速く、自己完結した小さなバイナリになります。EF Core もこれに対応するための仕組みを持っていますが、**現時点では実験的機能です。**

> [!WARNING]
> 公式ドキュメントは「NativeAOT とクエリの事前コンパイルはきわめて実験的な機能であり、まだ本番運用に適していない」「将来のバージョンでリリースされる最終的な機能に向けた基盤とみなすべき」と明記しています。**本番環境の EF Core アプリケーションを NativeAOT で発行することは推奨されていません。**

仕組みは **クエリの事前コンパイル (query precompilation)** です。ソースコードを静的に解析して EF Core の LINQ クエリを見つけ、C# の **インターセプター (interceptor)** を生成します。生成されたインターセプターには、そのクエリの最終的な SQL がリテラルとして埋め込まれます。実測で生成されたコードを確認すると、確かに SQL がそのまま入っていました。

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

`Microsoft.EntityFrameworkCore.Tasks` は MSBuild タスクを提供するパッケージで、`PublishAot` が `true` なら **発行時に自動でコンパイル済みモデルと事前コンパイル済みクエリの生成処理を実行します**。旧 `dotnet publish` のログには `Optimizing DbContext...` が出ましたが、その後にクエリ事前コンパイルのエラーで失敗しています。この開始ログだけを発行成功の根拠にはできません。生成のタイミングは MSBuild プロパティで制御できます。

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

実測では `CompiledModels/` 配下にコンパイル済みモデルが、`Generated/Program.EFInterceptors.AppDb.cs` にインターセプターが生成されました。

#### 最大の制約は「動的クエリが書けない」こと

事前コンパイルは静的解析なので、条件によって演算子を組み立てるクエリは扱えません。

```csharp
// 事前コンパイルできない
IQueryable<Blog> q = db.Blogs.OrderBy(b => b.Id);
if (applyFilter) q = q.Where(b => b.Name != "foo");
return await q.ToListAsync();
```

このコードのまま `dotnet ef dbcontext optimize --precompile-queries` を実行すると、次のエラーで失敗します（実測）。

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
> 実測では、`EnsureCreated()` を含むコードを NativeAOT で発行すると `IL3050` の警告が出ました。マイグレーション操作は設計時モデルの構築を必要とするため NativeAOT ではサポートされておらず、マイグレーションバンドルなど別の手段で適用する必要があります。**現時点の NativeAOT 対応は多数の警告を伴うことが公式にも明記されており、発行が通らない場面もあります。** 実験目的にとどめてください。

#### NativeAOT なしで事前コンパイルだけ使う

NativeAOT を使わない通常の発行でも、対応するクエリを事前コンパイルして起動時のクエリコンパイルを省く方法があります。以下の追試は `PublishAot=false` で行いました。

```bash
dotnet ef dbcontext optimize --precompile-queries
```

静的クエリだけの最小アプリケーションでは、生成・通常発行・実行が成功し、実行時の `QueryCompilationStarting` は 0 件でした。一方、動的クエリを混在させた対照では生成コマンドが失敗し、通常発行後の静的・動的クエリはいずれも実行時にコンパイルされました。動的クエリを含むアプリケーションで「対応する部分だけ自動的に事前化される」とは考えないでください。

## 3. 実行時のコストを下げる

### DbContext プーリングでインスタンスを使い回す

`AddDbContext` の代わりに `AddDbContextPool` を使うと、`DbContext` インスタンスを再利用するプールが有効になります。インスタンスの生成と内部サービスの初期化を繰り返すコストを抑える仕組みですが、効果の大きさは処理内容によって変わります。スコープを作って `DbContext` を取得し、`Blogs.Local.Count` にアクセスして破棄する処理を 3,000 回繰り返して測ったところ、1 回あたり 0.141 ミリ秒・約 62 KB の割り当てが、0.0015 ミリ秒・512 バイトになりました。ただしこれはデータベースへクエリを送らず、コンテキストを使える状態にして返却・破棄する処理を比較した数値です。この測定だけで、エンドポイント全体の応答時間の改善率を判断することはできません。

エンドポイント全体でどれだけ変わるかも負荷試験で確かめました。ASP.NET Core から SQL Server に対して主キー 1 件を読むだけの軽いエンドポイントを用意し、`AddDbContext` と `AddDbContextPool` だけを入れ替えて、同時 100 リクエストで 15 秒ずつ測定した結果です。

| 登録方法 | スループット（測定が安定したあとの 2 回） |
| --- | --- |
| `AddDbContext` | 8,186 req/s / 8,482 req/s |
| `AddDbContextPool` | 9,506 req/s / 9,550 req/s |

**改善は約 14 パーセントでした。** 前述のクエリを発行しない測定での差（約 94 倍）とは桁が違います。この測定では軽いクエリを高い並列数で実行しました。効果をこの条件だけに限定したり、重いクエリでは必ず効果がないと考えたりせず、実際のエンドポイント全体で測定してください。公式ドキュメントも「`DbContext` は一般に軽いオブジェクトで、生成と破棄にデータベース操作は伴わず、**ほとんどのアプリケーションでは性能に目立った影響なく生成できる**」としたうえで、内部サービスの初期化コストが問題になるのは**高性能が求められる場面**だと限定しています。

```csharp
builder.Services.AddDbContextPool<BloggingContext>(
    options => options.UseSqlServer(connectionString),
    poolSize: 1024);
```

`poolSize` は保持するインスタンスの最大数で、既定は 1024 です。プールが空の場合は新しいインスタンスが生成されるため、上限を超えても動作は継続します。実際に `poolSize: 2` を指定して 5 つのスコープを同時に保持したところ、3 つ目以降も例外にならず、待機によるブロックも発生しませんでした。プールの上限は「同時実行数の上限」ではなく「使い回すために保持しておく数の上限」だと理解してください。

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
> `AutoDetectChangesEnabled = false` のままプロパティを書き換えても、EF Core はその変更に気づきません。**例外は出ず、保存されないまま処理が成功します。** 10,000 件を追跡して 1 件だけ書き換え、無効のまま `SaveChangesAsync` を呼んだところ、影響行数は **0** でした。
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
> 公式ドキュメントは「性能を出すために自動変更検出を無効にしなければならない、と決めつけないでください」と述べています。実際に測ると、**10,000 件を追跡した状態で `DetectChanges()` にかかった時間は 6 ミリ秒**でした（SQL Server プロバイダー / Release ビルド / プロパティ 3 個のエンティティ）。これは読み込み直後、プロパティを変更する前に 1 回計測した値で、データ取得時間は含みません。ただし、件数だけでコストは判断できません。公式は、プロパティ数などの条件次第で数千件を追跡するアプリケーションでも問題になりうると説明しています。プロファイリングで変更検出が問題だと判明した場合にのみ検討してください。

エンティティ数が本当に多く、変更検出そのものを避けたい場合は、**変更追跡プロキシ (change-tracking proxies)** という選択肢があります。`Microsoft.EntityFrameworkCore.Proxies` パッケージを追加して `UseChangeTrackingProxies()` を呼ぶと、EF Core が `INotifyPropertyChanged` / `INotifyPropertyChanging` を実装する派生型を動的に生成し、プロパティが変わった瞬間に通知が飛ぶためスナップショットが不要になります。実測でも、`AutoDetectChangesEnabled = false` のままプロパティを書き換えて状態が `Modified` になることを確認しました。

導入時には、次の制約を確認してください。

> [!WARNING]
> 変更追跡プロキシを使うと、EF Core は **常にプロキシのインスタンスを追跡しなければなりません。** 元の型のインスタンスは通知を出さないため、変更が失われます。そのため新しいエンティティは `new` ではなく `CreateProxy` で作る必要があります。
>
> ```csharp
> // new で作ったインスタンスを Add すると実行時例外
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
> 加えて、エンティティ型は継承可能である必要があります。1 つでも条件を満たさないと、モデルの構築時点で失敗します。
>
> ```text
> InvalidOperationException: Property 'Blog.Id' is not virtual. 'UseChangeTrackingProxies'
> requires all entity types to be public, unsealed, have virtual properties, and have a
> public or protected constructor. 'UseLazyLoadingProxies' requires only the navigation
> properties be virtual.
> ```
>
> 最後の一文のとおり、**`virtual` が必要なプロパティの範囲は、遅延読み込みプロキシではナビゲーション、変更追跡プロキシではすべてのマップされたプロパティであり**、変更追跡プロキシのほうが要求が厳しい点に注意してください。

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

実際に 3 パターンを比べました。いずれも `AutoDetectChangesEnabled = false` にした状態で、読み込んだエンティティのプロパティを書き換えた直後の状態です（実測）。

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

`ChangedNotifications` でも元の値は保持されます。実測でも、書き換えた直後に `OriginalValue` から書き換え前の値を取得できました。スナップショットまで不要にしたい場合は `ChangingAndChangedNotifications` を選びますが、その場合 `INotifyPropertyChanging` の実装が必須になります。

> [!WARNING]
> 公式ドキュメントは、EF Core が要求するのは**すべてのプロパティ（ナビゲーションを含む）の通知**だと述べています。一部のプロパティだけ通知する実装は、EF Core との組み合わせでは正しく動きません。
>
> また、エンティティ型ごとに戦略を変えることもできますが、公式ドキュメントは「通知エンティティでない型のために結局 `DetectChanges` が必要になるので、たいていは逆効果」としています。

### バッファリングとストリーミング

`ToListAsync` は結果をすべてメモリに読み込みます（バッファリング）。大量の行を順次処理するだけなら、`await foreach` によるストリーミングでメモリ使用量を抑えられます。

効果は行数や列の値、処理方法によって変わります。SQLite に `Name` が 200 文字のエンティティを 200,000 行用意し、同期の `ToList` と `AsEnumerable` を比較しました。`GC.GetTotalMemory` を約 1 ミリ秒間隔で読み取ったマネージドヒープ増加量の最大値は、`ToList` が +121 MiB、ストリーミングが +6 MiB（約 20 分の 1）でした。これはプロセス全体のピークメモリではありません。別の 2,000 行の測定では所要時間が 2.42 ミリ秒と 2.39 ミリ秒でしたが、列の値や計測指標も異なるため、この件数を効果の境界とは考えないでください。結果全体を保持せず順次処理できる場合にストリーミングを検討します。

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
> これは実測でも明確に確認できます。SQL Server 上の 100,000 行（`Name` は 200 文字、`Url` は 1 文字）を `AsAsyncEnumerable` で列挙し、同じ方法で求めたマネージドヒープ増加量の最大値は次のとおりでした。
>
> | `EnableRetryOnFailure` | マネージドヒープ増加量の最大値 |
> | --- | --- |
> | 無効 | +7 MiB |
> | 有効 | +97 MiB |
>
> この表は `AsAsyncEnumerable` で列挙するときの再試行の有無を比較したもので、`ToList` とのメモリ使用量の差を測ったものではありません。内部バッファーがあることから、ストリーミングと `ToList` のメモリ使用量が常に同じだとは結論できません。
>
> 再試行を有効にしただけで約 14 倍に膨れ上がっています。大量の行を扱うクエリでは、そのクエリだけ再試行を無効にした `DbContext` を用意するか、`Skip`／`Take` によるページングで 1 回あたりの取得件数を抑えてください。

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
> MARS 無効のこの条件では、`AsAsyncEnumerable()` で列挙しても、1 件目を受け取った時点で大きなメモリ増加がありました。**分割クエリをストリーミングすれば、常に小さいメモリで処理できるわけではありません。**
>
> なお、この内部バッファリングは LINQ 演算子によるバッファリングとは別に発生します。公式ドキュメントも「再試行実行戦略が有効な状態で `ToList` を使うと、結果セットは**メモリに 2 回読み込まれる**（EF による内部の 1 回と `ToList` による 1 回）」と注意しています。

### 非同期 API を使う

同期版の `ToList()` や `SaveChanges()` は、データベースの応答を待つ間スレッドをブロックします。ASP.NET Core ではスレッドプールが枯渇し、スループットが大きく低下する原因になります。原則として非同期版を使い、`.Result` や `.Wait()` によるブロッキングを避けてください。公式ドキュメントも、**同期と非同期のコードを同じアプリケーションで混在させない**よう警告しています。気づかないうちにスレッドプールの枯渇を招きやすいためです。

この差がどれくらいになるかを負荷試験で実測しました。ASP.NET Core に同期版と非同期版のエンドポイントを 1 つずつ用意し、どちらも SQL Server に対して同じクエリを実行します。データベース側の待ち時間をそろえるため、クエリの先頭に `WAITFOR DELAY '00:00:00.100'` を入れて 100 ミリ秒の入出力待ちを作りました。10 コアのマシンで同時リクエスト数を変えて測定しました。5 回のウォームアップ後、15 秒間は新しい要求を開始し、送信済みの要求がすべて完了するまで待ちます。スループットの分母は、この待機も含む総経過時間です。

| 同時リクエスト数 | 非同期のスループット | 同期のスループット | 非同期の p99 | 同期の p99 |
| --- | --- | --- | --- | --- |
| 10 | 89 req/s | 89 req/s | 144 ミリ秒 | 135 ミリ秒 |
| 50 | 427 req/s | 123 req/s | 141 ミリ秒 | 767 ミリ秒 |
| 100 | 948 req/s | 129 req/s | 114 ミリ秒 | 1,327 ミリ秒 |
| 200 | 1,862 req/s | 107 req/s | 118 ミリ秒 | 2,958 ミリ秒 |

**今回の測定では、同時 10 のスループットはほぼ同程度で、同時 50 以上の測定点で差が現れました。** 同時実行数が CPU のコア数を超えることを普遍的な境界とは考えないでください。測定した範囲では非同期版のスループットが伸びた一方、同期版は同時 50〜200 で約 107〜129 req/s にとどまり、並列数を増やした測定点ほど p99 応答時間が長くなりました。同時 200 では p99 応答時間が 25 倍に開きました。エラーは 1 件も出ていないので、監視していても「遅い」としか見えない点が厄介です。

公式ドキュメントはこの理由を「同期 API はデータベースの入出力の間スレッドをブロックするため、必要なスレッド数と、発生するスレッドのコンテキストスイッチの回数が増える」と説明しています。

> [!WARNING]
> **例外があります。** EF Core の公式パフォーマンスガイダンスは、SQL Server 用のドライバーである `Microsoft.Data.SqlClient` の非同期実装に**既知の問題がある**ことを明記しています。原因不明の性能問題が出ている場合、**特に大きなテキストやバイナリの値を扱うときは、同期のコマンド実行を試してみる**よう案内しています。公式が挙げている Issue は [dotnet/SqlClient#593](https://github.com/dotnet/SqlClient/issues/593)（大きなデータの非同期読み取りが極端に遅い）と [dotnet/SqlClient#601](https://github.com/dotnet/SqlClient/issues/601) で、**いずれも本書の執筆時点で未解決 (Open) のままです。**
>
> 実際に Azure 上の SQL Server 2022 に `varbinary(max)` の 4 MiB の行を 20 件（合計 80 MiB）格納し、`AsNoTracking()` で全件読み取って比較しました。
>
> | 読み取る内容 | 同期 `ToList()` | 非同期 `ToListAsync()` |
> | --- | --- | --- |
> | 4 MiB のバイナリ × 20 行（80 MiB） | 1,623 / 2,029 / 1,955 ミリ秒 | 2,327 / 2,515 / 2,599 ミリ秒 |
> | 短い文字列の列だけを投影 | 9 ミリ秒 | 7 ミリ秒 |
>
> この測定では、大きなバイナリで 3 回とも非同期のほうが遅く、その比は 1.2〜1.4 倍でした。短い文字列だけを投影した測定では、同期 9 ミリ秒、非同期 7 ミリ秒で、同じ傾向は観測されませんでした。この結果だけで、ほかのクエリや環境での性能差は判断できません。
>
> ただしこれは「非同期をやめてよい」という意味ではありません。同期版はスレッドをブロックするため、**Web アプリケーションでは既定を非同期のままにしてください。** 大きな BLOB を扱う特定のクエリで実測して差が大きい場合に限り、その箇所だけ同期実行を検討するというのが公式の趣旨です。なお測定値はネットワーク遅延を含む環境依存の値なので、必ず自分の環境で計測してから判断してください。

> [!NOTE]
> **Azure Cosmos DB は同期 I/O に対応せず、EF Core の同期 API は既定で例外になります。** Cosmos DB プロバイダーが内部で使う SDK は同期入出力を提供していません。EF Core 9.0 以降は、この利用を検出すると既定で例外にします。
>
> 実際に Azure 上の Azure Cosmos DB（NoSQL API）に対して EF Core 10 から実行したところ、次のようになりました。保存の比較では新しい `Order` を追加してから呼び出しています。例外欄は型とメッセージ要旨であり、全文の転記ではありません。
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
> EF Core 10.0.11、Microsoft.Data.SqlClient 6.1.6、SQL Server 2022 で、`ExecuteSqlRawAsync` による 20 秒の `WAITFOR` を対象に、呼び出す前からキャンセル済みの場合と、サーバーで待機中と確認してからキャンセルする場合を試しました。どちらも待機時間より前に例外で終了しました。公式の[SqlClient の非同期キャンセル試験](https://github.com/dotnet/SqlClient/blob/v6.1.6/src/Microsoft.Data.SqlClient/tests/ManualTests/SQL/SqlCommand/SqlCommandCancelTest.cs#L534-L560)も、待機時間より前に例外終了することを確認しています。例外型や停止までの時間が、すべての呼び出し経路で同じとは仮定しないでください。
>
> この試験では、明示トランザクション内で先に 1 行を更新していました。対象操作の例外終了を待ち、キャンセル済みトークンではなく `CancellationToken.None` を使った `RollbackAsync` とトランザクションの破棄を完了してから、別の物理接続で元の値が残っていることを確認しました。**キャンセル要求だけでロールバックまで完了すると確認した結果ではありません。**

### プロバイダーを替えるとモデルの意味が変わる

プロバイダーの差し替えは「接続先が変わるだけ」ではありません。Azure Cosmos DB のようなドキュメントデータベースでは、**同じ C# のモデルが別の意味に解釈されます。**

公式ドキュメントは「**関連するエンティティ型は既定で所有型 (owned) として構成される**。特定のエンティティ型でこれを防ぐには `ModelBuilder.Entity` を呼ぶ」と述べています。つまり、リレーショナルデータベースなら別テーブルになる子エンティティが、Azure Cosmos DB では**親ドキュメントの中に埋め込まれます。**

ここでは本編とは別に、`string` 型の `Id` を持つ最小の `Blog` / `Post` と、`DbSet<Blog>` だけを公開する Context を使いました。ブログ 1 件・投稿 1 件を保存し、新しい Context で読みます。独立型の対照では `b.Entity<Post>()` に加え、`HasMany(...).WithOne().HasForeignKey("BlogId")` で関係を明示しました。本編の両 `DbSet` を持つ Context の接続先だけを変更した実測ではありません。

| モデルの構成 | `Post.IsOwned()` | `Post` のコンテナー | `Include()` | `Include` なしで読んだ子 |
| --- | --- | --- | --- | --- |
| 既定（何も書かない） | `True` | なし（親に埋め込み） | 成功（そもそも不要） | **1 件** |
| `b.Entity<Post>()` と関係を明示 | `False` | `Db` | **例外** | 0 件 |

既定のままなら、`Include` を書かなくても子は一緒に読み込まれます。1 つのドキュメントとして格納されているためです。

一方、`ModelBuilder.Entity<Post>()` を呼んで独立したエンティティ型にすると、`Include` は次の例外になります。

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

`AddCosmos<TContext>(connectionString, databaseName)` の 2 つの文字列は、**接続文字列とデータベース名**です。エンドポイントとキーではありません。実測では、正しい組み合わせで Scoped のコンテキスト解決と読み取りに成功し、エンドポイントを接続文字列の位置へ渡す誤用は `ArgumentException` になりました。エンドポイントとキーを個別に渡す場合は、`AddDbContext` 内で対応する `UseCosmos(endpoint, key, databaseName)` を使います。接続値をソースコードへ埋め込まないでください。

公式の[Cosmos DB プロバイダーの接続オプション](https://learn.microsoft.com/ja-jp/ef/core/providers/cosmos/#azure-cosmos-db-options)には、一覧中のオプションを**すべて同時に使う意図ではない**と注意があります。

| 構成 | 実測結果と注意 |
| --- | --- |
| Gateway に Direct 専用の TCP 設定を指定 | `MaxRequestsPerTcpConnection` などの指定で `ArgumentException`。接続モードに適した設定だけを使う |
| `HttpClientFactory` と `GatewayModeMaxConnectionLimit` を併用 | 明示的な上限指定との併用で `ArgumentException`。ファクトリを使う場合の上限は `HttpClientHandler.MaxConnectionsPerServer` で構成する |
| Direct の `IdleTcpConnectionTimeout` を 1 分に設定 | 設定値の読み返しは成功しても、初回の入出力で `ArgumentOutOfRangeException`。公式下限の 10 分に直すと読み取りに成功 |

Gateway と Direct の両方で実通信を確認しましたが、接続数の上限まで負荷をかけたり、アイドル接続の回収時刻を測定したりした結果ではありません。**設定値を読み返せることだけを、通信まで成功する証拠にしないでください。**

#### コンテナーとパーティションキー

`HasDefaultContainer` はモデルの既定コンテナー、`ToContainer` はエンティティごとの保存先、`ToJsonProperty` は保存する JSON 名を指定します。実測では既定・個別の保存先と、変更した JSON 名を実ドキュメントで確認しました。

Cosmos DB では、異なるパーティションなら同じ `id` の文書を保存できます。単一パーティションキーのモデルで同じ `id` の文書を別々のパーティションへ保存し、`FindAsync` に **id とパーティションキーの両方**を渡して識別できることを確認しました。`WithPartitionKey` による検索先の限定も、[公式のクエリガイド](https://learn.microsoft.com/ja-jp/ef/core/providers/cosmos/querying)と実測結果が一致しています。

階層パーティションキーは、`HasPartitionKey(x => new { x.TenantId, x.UserId, x.SessionId })` のように構成します。`string` / `Guid` / `int` の 3 階層を構成し、同じ id の 4 文書を保存した実測では、id と全階層のキーを渡した `FindAsync` が目的の 1 件をポイント読み取りしました。

また、LINQ の等値条件を **先頭から連続するキー**に指定すると、その値は SQL の条件から取り出され、実行ログの `Partition` に現れました。先頭 1 個・先頭 2 個・全 3 個でこの動作を確認しました。一方、中間だけ・末尾だけ・中間と末尾の条件は SQL 側に残り、同じパーティション指定にはなりませんでした。先頭キーの有無を無視して、同じ検索先に限定できると考えないでください。

#### ETag と SDK 経由の更新

`UseETagConcurrency()` はシャドウ状態の `_etag` を同時実行トークンにし、`IsETagConcurrency()` は ETag を CLR プロパティへマッピングする場合に使います。両方式とも、別の `DbContext` が先に同じ文書を更新すると、古い ETag で保存した側は HTTP 412 に基づく `DbUpdateConcurrencyException` になりました。[公式の ETag による楽観的同時実行制御](https://learn.microsoft.com/ja-jp/ef/core/providers/cosmos/modeling#optimistic-concurrency-with-etags)に対応する挙動です。

また、`Database.GetCosmosClient()` から取得した SDK で文書を更新しても、**コンテキストが追跡済みの値は自動更新されません**。実測では追跡値が古いまま、新しい `DbContext` は更新後の値を読みました。SDK 経由の更新を EF Core の変更追跡に通知する仕組みと考えないでください。

#### 有効期限とスループットの構成

**TTL (Time to Live)** は最終更新からの有効期間を秒数で指定します。`HasDefaultTimeToLive` によるコンテナーの既定値と、JSON の `ttl` にマッピングした項目ごとの値を分けて扱います。既定 8 秒、個別 4 秒、個別 `-1` の 3 条件で保存すると、11 秒待機後の読み取りは前の 2 条件が HTTP 404、`-1` の項目は取得可能でした。`-1` は有効期限を無効にする指定です。この実測は、物理削除の完了時刻を測定したものではありません。

スループットも、データベース共有とコンテナー専用では構成する対象が異なります。新規作成時の実測では、`ModelBuilder` の `HasManualThroughput(400)` と `HasAutoscaleThroughput(1000)` で、それぞれデータベースの手動 400 RU/s と自動スケール最大 1000 RU/s の設定を確認しました。エンティティ側の `HasManualThroughput(500)` ではコンテナー専用の手動 500 RU/s を確認しました。これは構成値の確認であり、負荷に応じたスケール動作や応答性能の測定ではありません。

#### トリガーは全クライアントに強制されない

EF Core 10 の Cosmos DB プロバイダーでは、作成済みのトリガーを `HasTrigger` で Pre / Post と操作種別に対応付けられます。実測では、Pre の Create / Replace が文書を書き換え、Post の Delete で例外を発生させると削除がロールバックしました。

> [!WARNING]
> **トリガーを認証や監査の強制機構にしないでください。** SDK からトリガーを指定せず実行した操作は成功しました。公式の[データベーストリガー](https://learn.microsoft.com/ja-jp/ef/core/providers/cosmos/modeling#database-triggers)も、直接アクセスするクライアントはトリガーを省略できるため、セキュリティ関連機能の強制に使わないよう注意しています。SQL Server のトリガーと同じ強制力を想定しないでください。

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

新しいコンテナーを作成し、`SaveChangesAsync()` で本文と 3 次元の `float[]` を持つ 6 文書を保存して、検索と再取得を確認しました。これは通常のインデックス指定とは異なる、全文検索用とベクトル検索用の設定です。既存コンテナーのポリシーを自動更新する手順ではありません。コンテナー作成時の認証上の注意は[付録3のマイグレーションの制限](../appendix-efcore-03/index.md#1-マイグレーションの詳細)を参照してください。

> [!WARNING]
> **`Flat` ベクトル索引の上限は 505 次元です。** 公式サービスガイドに明記されており、実測でも 505 次元は成功、506 次元と 1536 次元はコンテナー作成時に HTTP 400 になりました。EF の公式ページにある「1536 次元 + `Flat`」の組み合わせをそのまま使わないでください。`QuantizedFlat` の 1536 次元では作成と検索が成功しましたが、6 文書の結果から索引の性能や近似検索の利用まで判断することはできません。

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

`FullTextScore` は通常の数値計算メソッドとして使うものではありません。**公式は並べ替えでの利用に限定**しており、`Select` に投影したり `Where` の条件に直接使ったりすると、実測でも HTTP 400 になりました。

既定言語は `en-US` です。プロパティごとの `EnableFullTextSearch("de-DE")`、およびモデル全体の `modelBuilder.HasDefaultFullTextLanguage("de-DE")` も実測しました。**既定言語の設定先は `EntityTypeBuilder` ではなく `ModelBuilder`**です。多言語対応のプレビュー・リージョン条件は Azure 側の公式ガイドを確認してください。今回のドイツ語での成功を、全言語・全リージョンでの利用保証にはできません。

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

**RRF (Reciprocal Rank Fusion)** は、複数の検索結果の順位を統合する方法です。全文検索とベクトル検索を別々に実行してアプリケーション側で結合するのではなく、上の式を Cosmos DB の検索へ変換できます。重みを変えた実測では、全文検索を優先する場合とベクトル検索を優先する場合で上位の文書が変わりました。

> [!NOTE]
> **重みには `double[]` を渡してください。** `new[] { 1, 2 }` は `int[]` と推論され、コンパイルエラーになります。公式 API リファレンスの引数型も `double[]` です。また、全文検索得点だけ、ベクトル検索得点だけ、3 つの得点関数を使った RRF も実行できました。ただし、ここで確認したのは設定と問い合わせの動作であり、AI の回答精度や大規模データでの性能向上ではありません。

---

### 同時実行検出を無効にしてはいけない

1 つの `DbContext` インスタンスで操作が重複すると、EF Core の **同時実行検出 (concurrency detection)** によって `InvalidOperationException` が発生することがあります。未検出なら安全という意味ではありません。この検出は `DbContextOptionsBuilder.EnableThreadSafetyChecks(false)` で無効にできます。公式ドキュメントは「わずかな性能向上が得られるが、`DbContext` インスタンスが同時に使われた場合の **動作は未定義になり、プログラムは予測できない形で失敗する可能性がある**」と説明し、「性能向上が相当なものであることを確認し、アプリケーションを同時実行のバグについて十分にテストしたうえでのみ無効化すること」と釘を刺しています。

実際に SQL Server 2022 に対して、同じ `DbContext` インスタンスから 2 本のクエリを `Task.Run` で並行実行する処理を、検出の有無を変えて 3 回ずつ試したところ、次の結果になりました（実測）。例外メッセージは先頭行または要旨で、完全なスタックトレースではありません。

| 同時実行検出 | 発生した例外 |
| --- | --- |
| 有効（既定） | 3 回とも `InvalidOperationException: A second operation was started on this context instance before a previous operation completed.`（原因と対処ページへのリンク付き） |
| 無効 | 1 回目: `InvalidOperationException`（接続が閉じられていない旨）<br>2 回目: `InvalidOperationException`（同上、接続状態の表示だけが異なる）<br>3 回目: `InvalidCastException: Unable to cast object of type 'Microsoft.Data.ProviderBase.DbConnectionClosedConnecting' to type 'Microsoft.Data.SqlClient.SqlInternalConnectionTds'.` |

この 3 回の観測では、検出を無効にすると接続状態の例外や型変換の例外が出ました。ただし、実行ごとに必ず異なる例外が出るという仕様ではありません。公式は同時利用時の動作を未定義としているため、この例外の種類や順序に依存せず、原則として検出を既定のままにしてください。

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

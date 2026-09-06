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
   - [ログとセキュリティ](#ログとセキュリティ)
   - [DbContext の構成と接続](#dbcontext-の構成と接続)
2. [クエリとモデルの最適化](#2-クエリとモデルの最適化)
   - [インデックスを正しく張る](#インデックスを正しく張る)
   - [コレクションのパラメーター化と IN 句の翻訳](#コレクションのパラメーター化と-in-句の翻訳)
   - [コンパイル済みクエリ](#コンパイル済みクエリ)
   - [コンパイル済みモデル](#コンパイル済みモデル)
   - [NativeAOT と事前コンパイル済みクエリ](#nativeaot-と事前コンパイル済みクエリ)
3. [実行時のコストを下げる](#3-実行時のコストを下げる)
   - [DbContext プーリングでインスタンスを使い回す](#dbcontext-プーリングでインスタンスを使い回す)
   - [変更検出のコストを理解する](#変更検出のコストを理解する)
   - [バッファリングとストリーミング](#バッファリングとストリーミング)
   - [非同期 API を使う](#非同期-api-を使う)
   - [同時実行検出を無効にしてはいけない](#同時実行検出を無効にしてはいけない)
4. [参考ドキュメント](#4-参考ドキュメント)

---

## 1. 計測と診断

> [!NOTE]
> **この付録に載せた実測値の測定条件について。** 以下に出てくる数値は、次の環境で測定したものです。数値そのものはハードウェア・データ量・ネットワークによって大きく変わるため、**傾向（どちらが速いか、桁がいくつ違うか）を読み取る材料**として扱い、自分のアプリケーションでは必ず自分で計測してください。
>
> | 項目 | 値 |
> | --- | --- |
> | マシン | Apple M1 Max（10 コア）、macOS 26.6 |
> | .NET SDK | 10.0.400 |
> | EF Core | 10.0.11 |
> | SQLite | ローカルファイル（同一マシン） |
> | SQL Server | SQL Server 2022（Linux）、2 vCPU / 4 GB、Azure 上のリモート接続 |
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

`ToQueryString()` を使うと、実行せずに生成される SQL を確認できます。SQL Server ではパラメーターの `DECLARE` が先頭に付くため、出力をそのまま SQL Server Management Studio に貼り付けて実行できます。

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

SQL Server 2022 に対して実行した結果は次のとおりです。`ToQueryString()` と違って `CommandText` にはパラメーターの値が埋め込まれず、`Parameters` コレクションから個別に取得します。

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

同じクエリを既定の形式と比べました（実測）。

```text
--- 既定 ---
      Executed DbCommand (0ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      SELECT "p"."Id", "p"."Name"
      FROM "Plains" AS "p"

--- UtcTime | SingleLine ---
2026-09-05T15:42:01.7745760Z -> Executed DbCommand (0ms) [Parameters=[], ...]SELECT "p"."Id", "p"."Name"FROM "Plains" AS "p"
```

`UtcTime` を付けると ISO 8601 の UTC タイムスタンプが先頭に付き、`SingleLine` を付けると改行が取り除かれて 1 行になります。

> [!WARNING]
> `SingleLine` は改行を**取り除くだけ**で、空白に置き換えるわけではありません。実測のとおり `...CommandTimeout='30']SELECT` のように語がつながります。人が読むログには向きません。**構造化ログの取り込み用**と考えてください。

> [!TIP]
> 既定では時刻が出力されません。時刻を入れたい場合は `DefaultWithUtcTime` や `DefaultWithLocalTime` を使うと、既定の内容に時刻だけを足せます。

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
active_dbcontexts                     = 0
compiled_query_cache_hits             = 2
compiled_query_cache_misses           = 2
execution_strategy_operation_failures = 0
optimistic_concurrency_failures       = 0
queries                               = 4
savechanges                           = 1
```

`misses` が 2 なのは、**クエリの形が 2 種類**あったからです。同じ形の 2 回目と 3 回目は `hits` に入っています。**`misses` がクエリ実行回数と同じ勢いで増えていたら、キャッシュがまったく効いていない**ことを意味します。動的に組み立てた式ツリーや、定数を埋め込んでしまったクエリが原因になりがちです。

```csharp
var meter = new MeterListener();
meter.InstrumentPublished = (instrument, listener) =>
{
    if (instrument.Meter.Name == "Microsoft.EntityFrameworkCore")
    {
        listener.EnableMeasurementEvents(instrument);
    }
};
meter.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
    Console.WriteLine($"{instrument.Name} = {value}"));
meter.Start();

// ここでクエリを実行する

meter.RecordObservableInstruments();
```

> [!WARNING]
> EF Core のメトリックはすべて**観測可能な計測器 (observable instrument)** です。値を取り出すには `RecordObservableInstruments()` を明示的に呼ぶ必要があります。これを忘れるとコールバックが 1 度も呼ばれず、「メトリックが取れない」と誤解しがちです（実測で確認）。

> [!TIP]
> `compiled_query_cache_misses` と `optimistic_concurrency_failures` は、そのまま監視のアラート条件にできます。前者はこの付録の「[コンパイル済みクエリ](#コンパイル済みクエリ)」で説明したキャッシュの効き具合を、後者は「[楽観的同時実行制御](../appendix-efcore-04/index.md#楽観的同時実行制御)」で説明した競合の発生頻度を表します。

### クエリタグでログと LINQ を結びつける

ログに大量の SQL が流れると、「この重いクエリはソースコードのどこが出しているのか」が分からなくなります。**クエリタグ (Query Tag)** を使うと、LINQ クエリに付けた注釈が SQL のコメントとしてそのまま出力されます。

```csharp
var blogs = await context.Blogs
    .TagWith("月次レポート用の集計")
    .ToListAsync();
```

生成される SQL は次のようになります（SQLite で実測。タグの出力はプロバイダーによらず同じ形式です）。

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

こちらも SQLite で実測したものです。

```sql
-- 1つ目

-- 2つ目

-- File: /path/to/Program.cs:6

SELECT "b"."Id", "b"."Name"
FROM "Blogs" AS "b"
```

SQL Server のクエリストアや実行計画の分析ツールでは、このコメントごとキャプチャされるため、データベース側で見つけた重いクエリからアプリケーションのコードへ一発でたどり着けます。

> [!NOTE]
> **Hibernate** にも、クエリに注釈を付ける `setComment()` があり、`hibernate.use_sql_comments` を有効にすると SQL のコメントとして出力されます。EF Core のクエリタグは、追加の設定なしに `TagWith` を呼ぶだけで有効になる点と、`TagWithCallSite()` で呼び出し位置を自動的に埋め込める点が異なります。

> [!TIP]
> パフォーマンス問題の多くは、EF Core 自体ではなく「不要な列を取りすぎている」「N+1 が起きている」「インデックスがない」という設計上の問題に起因します。ここまでで説明した `AsNoTracking`、投影、`Include`、`AsSplitQuery`、ページングを先に見直してください。

#### メトリクスで全体像をつかむ

個々のクエリを見る前に、アプリケーション全体の傾向を数値で押さえます。EF Core は `Microsoft.EntityFrameworkCore` という名前の [`Meter`](https://learn.microsoft.com/ja-jp/dotnet/core/diagnostics/metrics)（.NET の計測 API の単位。詳しくは「メトリックの概要」を参照）を通じて次のカウンターを公開しています（EF Core 10.0.11 で実際に列挙して確認）。

| メトリクス名 | 意味 |
| --- | --- |
| `microsoft.entityframeworkcore.active_dbcontexts` | 生存している `DbContext` の数 |
| `microsoft.entityframeworkcore.queries` | 実行されたクエリの累計 |
| `microsoft.entityframeworkcore.savechanges` | `SaveChanges` の累計 |
| `microsoft.entityframeworkcore.compiled_query_cache_hits` | クエリキャッシュにヒットした回数 |
| `microsoft.entityframeworkcore.compiled_query_cache_misses` | クエリキャッシュを外した回数 |
| `microsoft.entityframeworkcore.execution_strategy_operation_failures` | 再試行戦略が捉えた失敗の回数 |
| `microsoft.entityframeworkcore.optimistic_concurrency_failures` | 楽観的同時実行制御の競合回数 |

特に重要なのが **クエリキャッシュのヒット率** です。EF Core は LINQ 式から SQL への変換結果をキャッシュしており、起動直後を過ぎればヒット率はほぼ 100% になるはずです。`compiled_query_cache_misses` が増え続ける場合は、クエリの形が毎回変わってキャッシュが効いていないことを示します。

同じ処理を 50 回ずつ繰り返してヒット率を実測すると、次のようになりました。

| 書き方 | ヒット率 |
| --- | --- |
| 同じ形の LINQ クエリを繰り返す | 98% |
| `EF.Constant()` で値をインライン化する | 98% |
| 条件式を実行時に付け外しして形を変える | 92% |
| `FromSqlRaw` に文字列連結で SQL を組み立てる | **0%** |

> [!NOTE]
> **`EF.Constant()` はこのキャッシュのヒット率を下げません。** EF Core のクエリキャッシュのキーは LINQ 式ツリーの形で決まり、値が SQL に埋め込まれるかどうかは関係しないためです。`EF.Constant()` が圧迫するのは EF Core 側ではなく **データベース側のプランキャッシュ** で、こちらは EF Core のメトリクスからは観測できません。
>
> ただしこれは EF Core 9 以降の挙動です。EF Core 8 の実装では `EF.Constant()` がクエリキャッシュより前の段階で定数ノードを埋め込んでいたため、値が変わるたびに EF Core 側でもキャッシュミスが発生していました。EF Core 9 でこの処理はパイプラインの後段へ移されています。
>
> 逆に、生の SQL を文字列連結で組み立てるとヒット率は 0% になります。SQL 文字列そのものがキャッシュキーの一部だからです。`compiled_query_cache_misses` が増え続けているなら、まず生 SQL の組み立て方と、条件を動的に付け外ししている箇所を疑ってください。

`active_dbcontexts` が想定より多いままなら `DbContext` が破棄されずに残っている可能性があり、`optimistic_concurrency_failures` の増加は同時更新の競合が実際に起きていることを示します。これらは OpenTelemetry や Application Insights にそのまま送れます。

> [!TIP]
> **`active_dbcontexts` は「正常な状態」を先に知っておくと役に立ちます。** `AddDbContextPool` を使い、スコープごとに `DbContext` を取得してクエリを 1 回発行する処理を 10 分間で 2,800 回繰り返しながら、30 秒おきに値を記録したところ、次のようになりました。
>
> | 経過 | 反復回数 | マネージドヒープ | ワーキングセット | `active_dbcontexts` |
> | --- | --- | --- | --- | --- |
> | 42 秒 | 200 | 8.6 MB | 139.4 MB | 1 |
> | 292 秒 | 1,400 | 9.6 MB | 141.1 MB | 1 |
> | 600 秒 | 2,800 | 8.9 MB | 141.2 MB | 1 |
>
> マネージドヒープは GC のたびに 4 MB 台まで戻り、ワーキングセットの増加は 10 分間で約 1.8 MB にとどまりました。そして `active_dbcontexts` は**最初から最後まで 1 のまま**です。スコープの終了時に確実にプールへ返却されているためです。
>
> この値が反復回数に比例して増えていく場合は、`DbContext` を `using` や DI スコープの外で作って破棄し忘れている箇所があります。負荷をかけた状態でこのメトリクスが横ばいになるかどうかを、リリース前に一度確認しておくとよいでしょう。

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

- **SQL の文字列を比較するテスト。** `ToQueryString()` の結果や、インターセプターで受け取った `DbCommand.CommandText` を期待値と突き合わせているコードは、すべて書き換えが必要です。`DbParameter.ParameterName` を直接見ているコードも同様です。
- **クエリプランのキャッシュ。** パラメーター名は SQL 文字列の一部なので、アップグレード直後は**ほぼすべてのプランが再コンパイルされます。** 公式も「大規模なシステムでは、配置直後に一時的なコンパイルのスパイクが起きることを見込んでおくべき」と述べています。負荷の高い時間帯を避けて配置してください。

### ログとセキュリティ

EF Core は、既定ではパラメーター値をログに出力しません。ログに残るのはパラメーター名だけで、値は `?` に置き換えられます。実際に SQL Server 2022 に対して `city` というローカル変数で絞り込んだところ、次のように出力されました（実測で確認）。

```text
[Parameters=[@city='?' (Size = 4000)], CommandType='Text', CommandTimeout='30']
SELECT [b].[Id], [b].[City], [b].[Name]
FROM [Blogs] AS [b]
WHERE [b].[City] = @city
```

> [!NOTE]
> **EF Core 10 でパラメーター名の付け方が変わりました。** EF Core 9 までは `__` プレフィックスと連番を付けた `@__city_0` のような名前でしたが、EF Core 10 では **元になった変数やメンバーの名前がそのまま使われ**、重複するときだけ末尾に数字が付きます。生成される SQL が読みやすくなり、ログやクエリプランをコードと対応付けやすくなったという理由です。
>
> ほとんどのアプリケーションではこの変更を意識する必要はありませんが、**生成された SQL の文字列を比較するスナップショットテストや、`DbCommand.CommandText` を解析するインターセプター・ロガーは修正が必要**です。
>
> なお、この簡素化が適用されるのは LINQ クエリのパラメーターです。`SaveChanges` が発行する `INSERT` / `UPDATE` は実測でも従来どおり `@p0`、`@p1` という名前でした。

ただし EF Core は、状況によってはパラメーターを送らずに値を SQL に **インライン化** することがあります。`EF.Constant()` を明示的に使った場合が代表例です。EF Core 9 まではインライン化された値がログにそのまま出力されていましたが、EF Core 10 以降は既定で `?` に置き換えられるようになりました。

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

EF Core 10 からは、接続文字列に `Application Name` が指定されていない場合、EF Core が自身と SqlClient のバージョン情報を含む値を **自動的に追加** します。実際に SQL Server プロバイダーで確認すると、次のように書き換えられます。

```text
[渡した接続文字列]
Server=localhost;Database=Test;Trusted_Connection=True;TrustServerCertificate=True

[EF Core が使う文字列]
Data Source=localhost;Initial Catalog=Test;Integrated Security=True;
Trust Server Certificate=True;Application Name="EFCore/10.0.11 (macOS 26.6.2 Arm64)"
```

ほとんどの場合は影響しませんが、**同じデータベースに EF Core と Dapper や ADO.NET などを併用している場合は注意が必要です。** SqlClient は接続文字列が異なると別の接続プールを使うため、両者が別々のプールに分かれます。この状態で `TransactionScope` を使うと、SqlClient が 2 つの異なるリソースとみなして、これまで不要だった **分散トランザクションへの昇格** を試みます。

> [!WARNING]
> **.NET 7 以降、暗黙の昇格は既定で無効化されています。** そのため実際に起きるのは「昇格して動き続ける」ことではなく、**その場で例外が投げられてアプリケーションが止まる** ことです。Windows Server 2022 + SQL Server 2022 + .NET 10 で `TransactionScope` の中から接続文字列の違う接続を 2 本開いたところ、2 本目の `Open()` で次の例外が発生しました。
>
> ```text
> System.NotSupportedException: Implicit distributed transactions have not been enabled.
> If you're intentionally starting a distributed transaction, set
> TransactionManager.ImplicitDistributedTransactions to true.
> ```
>
> `TransactionManager.ImplicitDistributedTransactions = true`（Windows 専用の API です）を設定したうえで同じコードを実行すると、2 本目の `Open()` の直後に `Transaction.Current.TransactionInformation.DistributedIdentifier` が `Guid.Empty` から実際の GUID に変わり、MSDTC (Microsoft Distributed Transaction Coordinator) への昇格が起きたことを確認できました。Linux や macOS では MSDTC 自体が存在しないため、この設定を有効にしても昇格はできません。

同じ環境で条件を変えて測ったところ、昇格するかどうかは次のように分かれました。

| `TransactionScope` 内での操作 | 昇格するか |
| --- | --- |
| 同じ接続文字列の接続を 2 本 **同時に** 開く | 昇格する |
| 同じ接続文字列で、1 本目を閉じてから 2 本目を開く | **昇格しない** |
| `Application Name` だけが違う接続を 2 本同時に開く | 昇格する |
| `Application Name` だけが違う接続を、1 本目を閉じてから 2 本目を開く | **昇格する** |

つまり、接続を律儀に閉じてから次を開く書き方をしていれば以前は昇格しなかったのに、EF Core 10 が `Application Name` を自動で足したことで昇格するようになる、というのがこの破壊的変更の実害です。

回避するには、接続文字列に `Application Name` を明示的に指定します。一度指定すると EF Core は上書きせず、渡した接続文字列がそのまま使われます。SQL Server 2022 に接続して `sys.dm_exec_sessions` の `program_name` を確認したところ、指定しない場合は `EFCore/10.0.11 (macOS 26.6.2 Arm64)`、明示した場合は `BloggingApi` となり、サーバー側から見える値も切り替わることを確認しています。

```json
{
  "ConnectionStrings": {
    "BloggingDatabase": "Server=...;Database=Blogging;Trusted_Connection=True;Application Name=BloggingApi"
  }
}
```

#### AddDbContext を 2 回呼ぶと後の設定が勝つ

`AddDbContext` を同じ `DbContext` 型に対して複数回呼び出したとき、**どちらの構成が使われるか**は EF Core 8 で変わりました。以前は最初の呼び出しが勝ちましたが、現在は**最後の呼び出しが勝ちます**。

```csharp
services.AddDbContext<BloggingContext>(o => o.UseSqlServer(first));
services.AddDbContext<BloggingContext>(o => o.UseSqlServer(second));
// → second が使われる
```

実際に接続文字列を変えて 2 回登録し、解決された `DbContext` の接続先を確認したところ、2 つ目の設定が使われました（実測）。これは `AddDbContext` が `TryAdd` ではなく通常の登録を使うようになったためで、公式ドキュメントは「他の `Add*` メソッドと一貫した動作になった」と説明しています。

> [!WARNING]
> ライブラリが内部で `AddDbContext` を呼んでいる場合、アプリケーション側の登録順によって構成が入れ替わります。**ライブラリの登録より後にアプリケーション側の登録を書く**のが安全です。

#### 構成を後から足す（ConfigureDbContext）

`AddDbContext` は「プロバイダーと接続文字列を決める」呼び出しです。これに対して、**ログや診断だけを後から足したい**ことがあります。テストで `EnableSensitiveDataLogging` を付けたい、共通ライブラリでインターセプターを差したい、といった場面です。

`AddDbContext` をもう一度呼ぶと、前述のとおり後の呼び出しが勝ってプロバイダーの構成ごと置き換わってしまいます。EF Core 9 以降は、このために **`ConfigureDbContext`** が用意されています。

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

公式ドキュメントによれば、`ConfigureDbContext` と `AddDbContext` は**呼び出した順に適用され、競合する設定は後の呼び出しが勝ちます**。競合しない設定（ログ、インターセプターなど）はすべて合成されます。呼ぶ順番は前後どちらでも構いません。

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
- 同じコード部分に複数のスレッドが入る可能性があるなら、**ファクトリーを注入して操作ごとに新しいインスタンスを作る**
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

EF Core のプロバイダーは **メジャーバージョンをまたいで動作しません**。公式のプロバイダー一覧には各プロバイダーが対応する EF Core のバージョンが載っていますが、この一覧は Microsoft 以外が提供するプロバイダーの最新状況に追いついていないことがあります。実際に NuGet の安定版を復元して確かめると、執筆時点では次のようになりました。

| パッケージ | 復元された安定版 | EF Core 10 のプロジェクトに追加した結果 |
| --- | --- | --- |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 10.0.3 | 警告なし。実際に PostgreSQL 16 へ接続してクエリと保存が動作 |
| `Pomelo.EntityFrameworkCore.MySql` | 9.0.0 | **`NU1608` 警告**（下記）。ビルドは通るが、実行すると例外で落ちる |

```text
warning NU1608: 依存関係の制約外で検出されたパッケージのバージョン:
Pomelo.EntityFrameworkCore.MySql 9.0.0 では
Microsoft.EntityFrameworkCore.Relational (>= 9.0.0 && <= 9.0.999) が必要ですが、
バージョン Microsoft.EntityFrameworkCore.Relational 10.0.11 は解決されました。
```

この警告を無視してそのまま MySQL 8 に接続すると、実行時に次の例外になります。**ビルドが通ることは動作の保証になりません。**

```text
System.MissingMethodException: Method not found:
'System.String Microsoft.EntityFrameworkCore.Diagnostics.AbstractionsStrings.ArgumentIsEmpty(System.Object)'.
```

つまり **公式一覧だけでも NuGet だけでも判断せず、両方を確認してください。** サードパーティー製プロバイダーを使うプロジェクトでは、EF Core のバージョンをプロバイダーの対応状況に合わせて決めるのが安全です。

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
> インデックスは読み取りを高速化する一方で、書き込み時のコストとストレージを増やします。すべての列にインデックスを張るのではなく、実際のクエリパターンに基づいて必要なものだけを作成してください。公式ドキュメントは、不要なインデックスを避けるほかに、**インデックスフィルターで対象行を絞り込んでオーバーヘッドを減らす**ことも挙げています。上の `HasFilter` の例は、検索対象が `Title IS NOT NULL` の行だけだとわかっている場合に、インデックスのサイズと更新コストを下げる手段でもあります。

#### インデックスが効く条件と効かない条件

公式ドキュメントは「インデックスがあれば必ず速くなるわけではない」として、いくつかの注意点を挙げています。SQL Server 2022 に 20,000 行のテーブルを作り、統計を更新したうえで `SET SHOWPLAN_ALL ON` で実行プランの物理演算子を確認したところ、いずれも公式の説明どおりの結果になりました。

| 絞り込みの書き方 | 生成される条件 | 物理演算子 |
| --- | --- | --- |
| `Title.StartsWith("T0001")` | `LIKE N'T0001%'` | **Index Seek** |
| `Title.EndsWith("A")` | `LIKE N'%A'` | Index Scan |
| 複合インデックス `(BlogId, Price)` を `BlogId` で絞る | `BlogId = 7` | **Index Seek** |
| 同じインデックスを `Price` **だけ**で絞る | `Price = 7` | Index Scan |
| 同じインデックスを両方で絞る | `BlogId = 7 AND Price = 7` | **Index Seek** |
| 列に対する式で絞る | `Price / 2 = 7` | Index Scan |

ここから読み取れることは 2 つあります。

1. **複合インデックスの列順序には非対称性がある。** 公式ドキュメントは「A と B の列にインデックスを張ると、A と B での絞り込みも、**A だけでの絞り込みも**高速化されるが、**B だけでの絞り込みは高速化されない**」と説明しています。上の表はこの説明をそのまま再現しています。`(BlogId, Price)` のインデックスは `Price` 単独の検索には使えないため、`Price` だけで絞り込むクエリが多いなら別のインデックスが必要です。
2. **列に式を適用すると単純なインデックスは使えなくなる。** `Price / 2 = 7` のように列を計算した結果で絞り込むと、`Price` にインデックスがあってもスキャンになります。公式の対処は、**永続化された計算列を定義してそこにインデックスを張る**ことです。

```csharp
public class Post
{
    public decimal Price { get; set; }
    public decimal HalfPrice { get; set; }
}

modelBuilder.Entity<Post>()
    .Property(p => p.HalfPrice)
    .HasComputedColumnSql("[Price] / 2", stored: true);

modelBuilder.Entity<Post>().HasIndex(p => p.HalfPrice);
```

同じ 20,000 行のテーブルでこの構成にして `WHERE [HalfPrice] = 7` を実行すると、物理演算子は **Index Seek** に変わりました。計算列については「[計算列](../appendix-efcore-02/index.md#計算列)」も参照してください。

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

### コレクションのパラメーター化と IN 句の翻訳

`Contains` でコレクションを絞り込み条件に使うと、EF Core はそれを `IN` 句へ変換します。**この変換方法は EF Core 10 で既定値が変わりました。**

```csharp
int[] ids = [1, 2, 3];
var blogs = await context.Blogs.Where(b => ids.Contains(b.Id)).ToListAsync();
```

| バージョン | 既定の翻訳 | 生成される SQL（要点） |
| --- | --- | --- |
| EF Core 9 まで | JSON 配列を 1 つのパラメーターとして送る | `WHERE [b].[Id] IN (SELECT [i].[value] FROM OPENJSON(@ids) WITH (...) AS [i])` |
| EF Core 10 以降 | 要素ごとに個別のパラメーターを送る | `WHERE [b].[Id] IN (@ids1, @ids2, @ids3)` |

SQL Server 2022 に対して実際に発行された SQL は次のとおりです。EF Core 9 までの方式では、配列全体が 1 つの `nvarchar` パラメーターとして渡され、`OPENJSON` で行に展開されていることが分かります。

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

新しい既定はデータベースのクエリプランナーに件数の情報を渡せるため、多くの場合はより良い実行プランが選ばれます。一方で、**要素数が毎回変わるとパラメーターの個数も変わり、SQL の形が変化するためクエリプランのキャッシュが効きにくくなります。** 要素数が数百から数千に及ぶコレクションを扱う場合は、以前の方式のほうが有利なこともあります。

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

実際に生成される SQL は次のとおりです（SQL Server プロバイダーでの実測）。

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
> クエリ単位で指定できるのは `EF.Parameter` と `EF.Constant` の 2 つだけで、`MultipleParameters` に対応するクエリ単位のメソッドはありません。既定がすでに `MultipleParameters` であるため、この方式を使いたい場合は**何も書かない**のが答えです。`DbContext` 全体で `ParameterTranslationMode.Parameter` などに変更したうえで、特定のクエリだけ既定に戻したいという場合は、`UseParameterizedCollectionMode` の設定自体をクエリごとに分けた `DbContext` で管理する必要があります。

| `ParameterTranslationMode` | 動作 |
| --- | --- |
| `MultipleParameters` | 要素ごとのパラメーター。EF Core 10 の既定 |
| `Parameter` | JSON 配列パラメーター 1 つ。EF Core 8・9 の既定 |
| `Constant` | 値を SQL に直接埋め込む。EF Core 7 までの既定 |

> [!TIP]
> `ParameterTranslationMode.Constant` と `EF.Constant()` は値を SQL に埋め込むため、要素数の組み合わせだけ異なる SQL が生成されます。**データベース側のプランキャッシュ** を圧迫するので、値の種類が少ないと分かっている場合に限って使ってください。なお、EF Core 自身のクエリキャッシュ（`compiled_query_cache_hits` / `misses`）は LINQ 式の形でキーが決まるため、`EF.Constant()` を使ってもヒット率は下がりません。実測でも、通常のパラメーター化と `EF.Constant()` はどちらも 98% で差がありませんでした。

### コンパイル済みクエリ

EF Core は同じ形のクエリに対して内部でクエリプランをキャッシュしますが、LINQ 式ツリーの走査とキャッシュキーの計算コストは毎回発生します。ホットパスのクエリでは **コンパイル済みクエリ** によりこのコストを削減できます。ただし削減されるのはこの前処理だけであり、効果は控えめです。SQLite に対する 1 件検索を 3,000 回繰り返して測ると、0.162 ミリ秒が 0.124 ミリ秒（約 1.3 倍速）になる程度でした。効果が見えるのは、同じクエリを毎秒何千回も実行するようなホットパスに限られます。

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
> この失敗は**コンパイル済みモデルを生成したときだけ**起きます。通常のモデルでは `private` のままでも動くため、パフォーマンス改善のために後から `dotnet ef dbcontext optimize` を導入した段階で初めて表面化します。

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

`Microsoft.EntityFrameworkCore.Tasks` は MSBuild タスクを提供するパッケージで、`PublishAot` が `true` なら **発行時に自動でコンパイル済みモデルと事前コンパイル済みクエリを生成します**。実測でも `dotnet publish` のログに `Optimizing DbContext...` が出力されました。生成のタイミングは MSBuild プロパティで制御できます。

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

NativeAOT の制約が厳しくても、事前コンパイル済みクエリだけを使って起動時間を短縮することはできます。

```bash
dotnet ef dbcontext optimize --precompile-queries
```

こちらであれば通常の（NativeAOT でない）アプリケーションとして発行できるため、動的クエリを含むアプリケーションでも、事前コンパイルできるクエリの分だけ起動コストを減らせます。

## 3. 実行時のコストを下げる

### DbContext プーリングでインスタンスを使い回す

`AddDbContext` の代わりに `AddDbContextPool` を使うと、`DbContext` インスタンスを再利用するプールが有効になります。インスタンスの生成と内部サービスの初期化コストが削減され、高スループットのアプリケーションでは有意な差が出ます。スコープの作成と `DbContext` の取得だけを 3,000 回繰り返して測ったところ、1 回あたり 0.141 ミリ秒・約 62 KB の割り当てが、0.0015 ミリ秒・512 バイトになりました。ただしこれは `DbContext` の生成コストだけを取り出した数値です。実際のリクエストではクエリの実行時間が大半を占めるため、エンドポイント全体の応答時間がこの比率で改善するわけではありません。

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
> context.Entry(blog).State;            // Unchanged（実際は変更済み）
> context.ChangeTracker.HasChanges();   // False（実際は変更あり）
>
> context.ChangeTracker.DetectChanges();
> context.Entry(blog).State;            // Modified
> await context.SaveChangesAsync();     // ここで初めて 1 行が保存される
> ```
>
> 無効にする場合は、`try` / `finally` で必ず元に戻し、その区間でプロパティを書き換えないことを保証してください。

> [!TIP]
> 公式ドキュメントは「性能を出すために自動変更検出を無効にしなければならない、と決めつけないでください」と述べています。実際に測ると、**10,000 件を追跡した状態で `DetectChanges()` にかかった時間は 6 ミリ秒**でした（SQL Server 2022 / Release ビルド / プロパティ 3 個のエンティティ）。数千件規模でボトルネックになることはまずありません。プロファイリングで変更検出が問題だと判明した場合にのみ検討してください。

エンティティ数が本当に多く、変更検出そのものを避けたい場合は、**変更追跡プロキシ (change-tracking proxies)** という選択肢があります。`Microsoft.EntityFrameworkCore.Proxies` パッケージを追加して `UseChangeTrackingProxies()` を呼ぶと、EF Core が `INotifyPropertyChanged` / `INotifyPropertyChanging` を実装する派生型を動的に生成し、プロパティが変わった瞬間に通知が飛ぶためスナップショットが不要になります。実測でも、`AutoDetectChangesEnabled = false` のままプロパティを書き換えて状態が `Modified` になることを確認しました。

ただし制約が多く、既定の選択肢にはなりません。

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
> 最後の一文のとおり、**遅延読み込みプロキシはナビゲーションプロパティだけが `virtual` であればよく**、変更追跡プロキシのほうが要求が厳しい点に注意してください。

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

重要なのは、**インターフェイスを実装しただけでは何も起きない**という点です。公式ドキュメントは「EF Core はこれらのインターフェイスが正しく実装されているかを検証できないため、自動的にはイベントを購読しない」と明記しています。モデル側で戦略を指定する必要があります。

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

3 つ目が重要です。**インターフェイスを実装しているのに `HasChangeTrackingStrategy` を呼び忘れると、通知はまったく使われません。** 例外も警告も出ないため、気づかないまま「変更が保存されない」状態になります。

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

効果は行数に強く依存します。1 行あたり約 250 バイトのエンティティを 200,000 行処理したときのピークメモリを測ると、`ToList` が +121 MB だったのに対し、ストリーミングは +6 MB（約 20 分の 1）でした。一方、2,000 行程度では両者にほとんど差がありません。ストリーミングを選ぶのは、結果全体を保持しなくてよい大量データの処理に限られます。

```csharp
await foreach (var post in context.Posts.AsNoTracking().AsAsyncEnumerable()
    .WithCancellation(cancellationToken))
{
    await ProcessAsync(post, cancellationToken);
}
```

> [!IMPORTANT]
> ストリーミング中は接続とデータリーダーが開いたままになります。ループの中で同じ `DbContext` に対して別のクエリを実行しないでください。
>
> また、`EnableRetryOnFailure` を有効にしている場合、公式ドキュメントは「再試行を有効にすると EF が結果セットを内部でバッファリングするため、大量の行を返すクエリではメモリ使用量が大きく増える可能性がある」と明記しています（[接続の回復性](https://learn.microsoft.com/ja-jp/ef/core/miscellaneous/connection-resiliency)）。つまり、再試行と併用するとストリーミングによるメモリ削減効果は得られません。
>
> これは実測でも明確に確認できます。SQL Server 上の 100,000 行（1 行あたり約 250 バイト）を `AsAsyncEnumerable` で列挙したときのピークメモリ増加量は次のとおりでした。
>
> | `EnableRetryOnFailure` | ピークメモリ増加量 |
> | --- | --- |
> | 無効 | +7 MB |
> | 有効 | +97 MB |
>
> 再試行を有効にしただけで約 14 倍に膨れ上がっています。大量の行を扱うクエリでは、そのクエリだけ再試行を無効にした `DbContext` を用意するか、`Skip`／`Take` によるページングで 1 回あたりの取得件数を抑えてください。

> [!WARNING]
> **分割クエリでも EF Core は内部でバッファリングします。** 公式ドキュメントは、EF が結果セットを内部でバッファリングするケースとして再試行実行戦略と分割クエリの 2 つを挙げ、分割クエリでは「**SQL Server で MARS が有効になっていない限り、最後のクエリ以外のすべての結果セットがバッファリングされる**」と説明しています。複数の結果セットを同時に開いておくことが通常できないためです。
>
> ブログ 200 件・投稿 50,000 件（1 件あたり約 250 バイト）を `AsSplitQuery()` と `AsAsyncEnumerable()` で列挙し、**1 件目を受け取った時点**のメモリ増加量を測った結果です。
>
> | 接続文字列 | 1 件目受信時のメモリ増加量 |
> | --- | --- |
> | 既定（MARS 無効） | **+58 MB** |
> | `MultipleActiveResultSets=True` | +1 MB |
>
> MARS 無効では、1 件目を受け取る前に投稿側の結果セットが丸ごと読み込まれています。つまり**分割クエリとストリーミングを組み合わせてもメモリは削減できません**。
>
> なお、この内部バッファリングは LINQ 演算子によるバッファリングとは別に発生します。公式ドキュメントも「再試行実行戦略が有効な状態で `ToList` を使うと、結果セットは**メモリに 2 回読み込まれる**（EF による内部の 1 回と `ToList` による 1 回）」と注意しています。

### 非同期 API を使う

同期版の `ToList()` や `SaveChanges()` は、データベースの応答を待つ間スレッドをブロックします。ASP.NET Core ではスレッドプールが枯渇し、スループットが大きく低下する原因になります。必ず非同期版を使い、`.Result` や `.Wait()` によるブロッキングを避けてください。公式ドキュメントも、**同期と非同期のコードを同じアプリケーションで混在させない**よう警告しています。気づかないうちにスレッドプールの枯渇を招きやすいためです。

> [!WARNING]
> **例外があります。** EF Core の公式パフォーマンスガイダンスは、SQL Server 用のドライバーである `Microsoft.Data.SqlClient` の非同期実装に**既知の問題がある**ことを明記しています。原因不明の性能問題が出ている場合、**特に大きなテキストやバイナリの値を扱うときは、同期のコマンド実行を試してみる**よう案内しています。公式が挙げている Issue は [dotnet/SqlClient#593](https://github.com/dotnet/SqlClient/issues/593)（大きなデータの非同期読み取りが極端に遅い）と [dotnet/SqlClient#601](https://github.com/dotnet/SqlClient/issues/601) で、**いずれも本書の執筆時点で未解決 (Open) のままです。**
>
> 実際に Azure 上の SQL Server 2022 に `varbinary(max)` の 4 MB の行を 20 件（合計 80 MB）格納し、`AsNoTracking()` で全件読み取って比較しました。
>
> | 読み取る内容 | 同期 `ToList()` | 非同期 `ToListAsync()` |
> | --- | --- | --- |
> | 4 MB のバイナリ × 20 行（80 MB） | 1,623 / 2,029 / 1,955 ミリ秒 | 2,327 / 2,515 / 2,599 ミリ秒 |
> | 短い文字列の列だけを投影 | 9 ミリ秒 | 7 ミリ秒 |
>
> 大きなバイナリでは 3 回とも非同期のほうが遅く、その比は 1.2〜1.4 倍でした。一方、短い値だけを取得するクエリでは差がありません。**問題が現れるのは大きな値を転送するときだけ**です。
>
> ただしこれは「非同期をやめてよい」という意味ではありません。同期版はスレッドをブロックするため、**Web アプリケーションでは既定を非同期のままにしてください。** 大きな BLOB を扱う特定のクエリで実測して差が大きい場合に限り、その箇所だけ同期実行を検討するというのが公式の趣旨です。なお測定値はネットワーク遅延を含む環境依存の値なので、必ず自分の環境で計測してから判断してください。

### 同時実行検出を無効にしてはいけない

1 つの `DbContext` インスタンスを複数のスレッドから同時に使うと `InvalidOperationException` になります。この例外を出しているのは EF Core の **同時実行検出 (concurrency detection)** という仕組みで、`DbContextOptionsBuilder.EnableThreadSafetyChecks(false)` で無効にできます。公式ドキュメントは「わずかな性能向上が得られるが、`DbContext` インスタンスが同時に使われた場合の **動作は未定義になり、プログラムは予測できない形で失敗する可能性がある**」と説明し、「性能向上が相当なものであることを確認し、アプリケーションを同時実行のバグについて十分にテストしたうえでのみ無効化すること」と釘を刺しています。

実際に SQL Server 2022 に対して、同じ `DbContext` インスタンスから 2 本のクエリを `Task.Run` で並行実行する処理を、検出の有無を変えて 3 回ずつ試したところ、次の結果になりました（実測で確認）。

| 同時実行検出 | 発生した例外 |
| --- | --- |
| 有効（既定） | 3 回とも `InvalidOperationException: A second operation was started on this context instance before a previous operation completed.`（原因と対処ページへのリンク付き） |
| 無効 | 1 回目: `InvalidOperationException`（接続が閉じられていない旨）<br>2 回目: `InvalidOperationException`（同上、接続状態の表示だけが異なる）<br>3 回目: `InvalidCastException: Unable to cast object of type 'Microsoft.Data.ProviderBase.DbConnectionClosedConnecting' to type 'Microsoft.Data.SqlClient.SqlInternalConnectionTds'.` |

検出を無効にすると、**実行のたびに違う低レベルの例外が出て、原因にたどり着けなくなります。** 公式が言う「予測できない形で失敗する」とはこのことです。この設定は原則として既定のままにしてください。

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

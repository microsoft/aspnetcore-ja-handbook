---
title: "付録 EF Core 3：マイグレーションの詳細とクエリ"
description: "EF Core のマイグレーションの読み方と運用、単一クエリと分割クエリ、照合順序、生の SQL、ユーザー定義関数とビューのマッピングを解説します。第8章の付録です。"
---

このページは [第8章：データベースアクセスと ORM (Entity Framework Core)](../08-entity-framework-core/index.md) の付録です。マイグレーションの詳細な扱い方と、クエリの制御、SQL を直接扱う方法を扱います。

本編を先に読んでから、必要な項目をここで参照してください。

**第8章のほかの付録**

- [付録 EF Core 1：モデル定義（エンティティとリレーションシップ）](../appendix-efcore-01/index.md)
- [付録 EF Core 2：モデル定義（キー・採番・SQL Server 固有）](../appendix-efcore-02/index.md)
- [付録 EF Core 4：更新・トランザクション・レプリカ](../appendix-efcore-04/index.md)
- [付録 EF Core 5：パフォーマンス](../appendix-efcore-05/index.md)
- [付録 EF Core 6：テスト](../appendix-efcore-06/index.md)


---

## 目次

1. [マイグレーションの詳細](#1-マイグレーションの詳細)
   - [生成されたマイグレーションを読む](#生成されたマイグレーションを読む)
   - [既定値の制約に名前を付ける](#既定値の制約に名前を付ける)
   - [EF Core ツールの使い分け](#ef-core-ツールの使い分け)
   - [SQL スクリプトとマイグレーションバンドルを使い分ける](#sql-スクリプトとマイグレーションバンドルを使い分ける)
   - [マイグレーションバンドルの対象ランタイムを指定する](#マイグレーションバンドルの対象ランタイムを指定する)
   - [モデルとマイグレーションのずれを検出する](#モデルとマイグレーションのずれを検出する)
   - [同時にマイグレーションが走らないようにする](#同時にマイグレーションが走らないようにする)
   - [特定のテーブルをマイグレーションの対象から外す](#特定のテーブルをマイグレーションの対象から外す)
   - [マイグレーション履歴テーブルをカスタマイズする](#マイグレーション履歴テーブルをカスタマイズする)
   - [初期データの投入（シード）](#初期データの投入シード)
   - [既存データベースからスキャフォールディングする](#既存データベースからスキャフォールディングする)
   - [設計時 DbContext ファクトリ](#設計時-dbcontext-ファクトリ)
2. [クエリの制御](#2-クエリの制御)
   - [単一クエリと分割クエリ](#単一クエリと分割クエリ)
   - [LeftJoin / RightJoin 演算子](#leftjoin--rightjoin-演算子)
   - [大文字小文字の区別は照合順序が決める](#大文字小文字の区別は照合順序が決める)
   - [複数の列で並べ替えるキーセットページング](#複数の列で並べ替えるキーセットページング)
   - [関連データを読み込まずに数える](#関連データを読み込まずに数える)
   - [存在チェックは Count ではなく Any を使う](#存在チェックは-count-ではなく-any-を使う)
   - [常に Include する（AutoInclude）](#常に-include-するautoinclude)
   - [null の比較は C# と SQL で意味が違う](#null-の比較は-c-と-sql-で意味が違う)
   - [エンティティをそのまま JSON にすると循環参照で失敗する](#エンティティをそのまま-json-にすると循環参照で失敗する)
3. [SQL を直接扱う](#3-sql-を直接扱う)
   - [生の SQL を使う](#生の-sql-を使う)
   - [ユーザー定義関数とビューをマッピングする](#ユーザー定義関数とビューをマッピングする)
   - [SQL Server 固有の関数を LINQ から呼ぶ](#sql-server-固有の関数を-linq-から呼ぶ)
4. [参考ドキュメント](#4-参考ドキュメント)

---

## 1. マイグレーションの詳細

> [!NOTE]
> **マイグレーションはリレーショナルデータベース専用の仕組みです。** Azure Cosmos DB のようなドキュメントデータベースには決まったスキーマがないため、公式ドキュメントは「スキーマのマイグレーションはサポートされない」「既存データベースからのリバースエンジニアリング（スキャフォールディング）もサポートされない」と明記しています。
>
> 次は、Cosmos プロバイダー専用プロジェクトのコンパイルと、Azure Cosmos DB（NoSQL API）に対する EF Core 10 の操作の確認例です。コンパイルエラーやモデル検証の例外は、サービスへのデータベース操作が成功したことを示す結果ではありません。
>
> | 書いたコード | 結果 |
> | --- | --- |
> | `db.Database.MigrateAsync()` | **コンパイルエラー** `CS1061`。`Migrate` 系の拡張メソッドはリレーショナルプロバイダー専用に定義されているため、Cosmos プロバイダーだけを参照したプロジェクトでは存在しない |
> | `db.Database.EnsureCreatedAsync()` | アカウントキー認証で成功。データベースとコンテナーが作成される |
> | `HasIndex(o => o.Customer)` | `InvalidOperationException: The entity type 'Order' has an index defined over properties 'Customer'. The Azure Cosmos DB provider for EF Core currently does not support index definitions.` |
>
> この制限は、リレーショナルデータベースのような**通常の `HasIndex` 定義**についてのものです。Azure Cosmos DB は既定のポリシーで項目を自動的にインデックス化します。一方、EF Core 10 は `IsFullTextIndex()` や `IsVectorIndex()` による**検索専用の索引設定**に対応しています。[付録5の「Cosmos DB の全文検索とベクトル検索」](../appendix-efcore-05/index.md#cosmos-db-の全文検索とベクトル検索)を参照してください。通常の索引定義は EF Core 8 までは無視され、EF Core 9 以降は例外になるため、検索専用の設定と区別してください。

> [!WARNING]
> **Microsoft Entra ID 認証で新しいデータベースを用意するときは、`EnsureCreatedAsync()` に頼らないでください。** ここでの RBAC はロールベースのアクセス制御 (Role-Based Access Control) を指します。[EF Core の Cosmos DB プロバイダーの公式説明](https://learn.microsoft.com/ja-jp/ef/core/providers/cosmos/)は、SDK の管理プレーン操作は RBAC に対応せず、`EnsureCreatedAsync` の代わりに Azure Management API を使うよう案内しています。
>
> 次は、キー認証を無効にした Azure Cosmos DB アカウントで `DefaultAzureCredential` を使い、データアクセス用ロールの有無とスコープを変える確認例です。
>
> | データアクセス用ロールとスコープ | 操作 | 実測結果 |
> | --- | --- | --- |
> | なし | 作成済みコンテナーのクエリ | `403 Forbidden` |
> | Data Contributor を対象コンテナーに限定 | 対象コンテナーへの保存・再読取・削除 | 成功 |
> | 同上 | 別コンテナーの読み取り | `403 Forbidden` |
> | Data Contributor をアカウント全体に付与 | 既存データのクエリ後、未作成 DB に `EnsureCreatedAsync()` | クエリは成功、DB 作成は `403 Forbidden` |
>
> この確認例のロールは、[Cosmos DB 組み込みデータ共同作成者](https://learn.microsoft.com/ja-jp/azure/cosmos-db/how-to-connect-role-based-access-control)（CLI のロール名は `Cosmos DB Built-in Data Contributor`、ロール定義 ID の末尾は `0002`）です。管理プレーンの権限とは別のもので、管理プレーンに `Owner` がある最初の条件でも、データアクセス用ロールがない場合のクエリ拒否を確認できています。
>
> 最後の条件の例外の抜粋は次のとおりです。**データのクエリができることと、SDK 経由で新しい DB を作成できることは別**です。ロールなしやスコープ外での `readMetadata` 権限不足とは区別し、拒否された操作まで確認してください。
>
> ```text
> CosmosException: Response status code does not indicate success: Forbidden (403); Substatus: 5302;
> message : Request blocked by Auth <アカウント名> : Request for Read DatabaseAccount is blocked
> because principal [<オブジェクト ID>] does not have required RBAC permissions
> to perform action [Microsoft.DocumentDB/databaseAccounts/sqlDatabases/write] on any scope.
> ```
>
> パスワードレス認証では、コンテナー作成をアプリケーションから切り離し、Azure CLI や Bicep などのインフラ側で行ってください。次は、管理プレーンの作成権限を持つ運用担当者が、サインイン済みの Azure CLI から既存のアカウントと SQL データベースにコンテナーを作成する例です。作成後にデータアクセス用ロールを使うアプリケーションから読み書きできることも、この構成で確認できています。山括弧の 4 項目は実際の名前に置き換えてください。
>
> ```bash
> az cosmosdb sql container create -a <アカウント名> -g <リソースグループ> \
>   -d <データベース名> -n <コンテナー名> --partition-key-path "/id" --throughput 400
> ```
>
> `/id` は確認例のモデルに合わせた指定です。このモデルでは文字列の `Id` を `HasPartitionKey` でパーティションキーにし、`ToContainer` でコンテナー名を指定しています。[EF Core 側のモデル設定](https://learn.microsoft.com/ja-jp/ef/core/providers/cosmos/modeling#partition-keys)に合わせて、CLI 側のパーティションキーパスも指定してください。`/id` が EF Core の既定値という意味ではありません。
>
> なお認証方式にかかわらず、公式ドキュメントは `EnsureCreatedAsync` について「**デプロイ時にのみ呼ぶこと。通常の運用で呼ぶとパフォーマンスの問題を起こしうる**」とも注意しています。アプリケーションの起動処理に入れたままにしないでください。

### 生成されたマイグレーションを読む

生成されるマイグレーションは通常の C# コードです。内容を確認し、必要なら手を入れられます。

```csharp
public partial class AddPostPublishedAt : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "PublishedAt",
            table: "Posts",
            type: "datetimeoffset",
            nullable: true);

        migrationBuilder.Sql("UPDATE [Posts] SET [PublishedAt] = [CreatedAt] WHERE [PublishedAt] IS NULL;");

        migrationBuilder.AlterColumn<DateTimeOffset>(
            name: "PublishedAt",
            table: "Posts",
            type: "datetimeoffset",
            nullable: false,
            defaultValue: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            oldClrType: typeof(DateTimeOffset),
            oldType: "datetimeoffset",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Posts_PublishedAt",
            table: "Posts",
            column: "PublishedAt");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Posts_PublishedAt", table: "Posts");
        migrationBuilder.DropColumn(name: "PublishedAt", table: "Posts");
    }
}
```

`Up` が適用時、`Down` がロールバック時の処理です。この例は、既存の `CreatedAt` が非 NULL の `datetimeoffset` 列であることを前提に、生成されたコードへデータ移行を加えたものです。[公式のデータ移行の手順](https://learn.microsoft.com/ja-jp/ef/core/managing-schemas/migrations/managing#transform-existing-data)に従い、**NULL 許容で追加 → `Sql` で既存データを埋める → 非 NULL 化**の順に処理します。最初から非 NULL と既定値を指定して追加すると、既存行にも既定値が入り、`WHERE [PublishedAt] IS NULL` に一致する行がなくなります。

上の `defaultValue` は、移行後に列の値を省略して挿入する行に対する既定値です。既存行はこの固定値ではなく、それぞれの `CreatedAt` で埋めます。列の既定値を維持する場合は、モデル側の `HasDefaultValue` も同じ値に合わせます。

EF Core 10.0.11 でこの `Up` から生成した SQL を Azure SQL Database の既存 2 行に適用した確認例でも、それぞれの `CreatedAt` が `PublishedAt` に引き継がれています。移行後の明示的な NULL 挿入は制約違反になり、`PublishedAt` を省略した新規行には指定した既定値が入ることを確認できています。

> [!WARNING]
> プロパティ名を変更した場合、EF Core は「列の削除」と「列の追加」として差分を検出することがあります。そのまま適用するとデータが失われるため、生成されたマイグレーションを確認し、必要に応じて `migrationBuilder.RenameColumn(...)` に書き換えてください。公式ドキュメントも「どの適用方法を選ぶ場合でも、必ず生成されたマイグレーションを検査し、本番データベースに適用する前にテストすること」と明記しています。

### 既定値の制約に名前を付ける

列に既定値を設定すると、SQL Server 側には **既定値制約 (default constraint)** が作られます。名前を指定しない場合、制約名はデータベースが自動生成します。次は、名前を指定しないテーブルを `sys.default_constraints` で確認した例です。

```text
DF__Posts__CreatedDa__49C3F6B7
DF__Posts__Views__4AB81AF0
```

自動生成された名前は固定名として扱わず、対象データベースで確認する必要があります。名前が分からないと、後から `ALTER TABLE ... DROP CONSTRAINT` で外すのが面倒になります。

EF Core 10 では、`HasDefaultValueSql` / `HasDefaultValue` の第 2 引数で制約名を指定できるようになりました。

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Post>()
        .Property(p => p.CreatedDate)
        .HasDefaultValueSql("GETDATE()", "DF_Post_CreatedDate");
}
```

生成されるマイグレーションには注釈として制約名が入り、SQL Server 向けの DDL では `CONSTRAINT` 句として出力されます。

```sql
CREATE TABLE [Posts] (
    [Id] int NOT NULL IDENTITY,
    [CreatedDate] datetime2 NOT NULL CONSTRAINT [DF_Post_CreatedDate] DEFAULT (GETDATE()),
    [Views] int NOT NULL DEFAULT 0,
    CONSTRAINT [PK_Posts] PRIMARY KEY ([Id])
);
```

名前を指定しなかった `Views` 列には `CONSTRAINT` 句が付いていない点に注目してください。列ごとに名前を付けて回るのが面倒な場合は、`UseNamedDefaultConstraints()` でモデル全体の既定値制約に EF Core が自動で名前を付けられます。

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.UseNamedDefaultConstraints();
}
```

これを有効にした同じモデルの確認例では、SQL Server 向けの DDL に両方の既定値制約の名前が含まれています。

```sql
[CreatedDate] datetime2 NOT NULL CONSTRAINT [DF_Posts_CreatedDate] DEFAULT (GETDATE()),
[Views] int NOT NULL CONSTRAINT [DF_Posts_Views] DEFAULT 0,
```

> [!WARNING]
> **この `int` の構成では、`0` を明示しても「未設定」と区別されず、データベースの既定値が使われます。** `Count` の初期値も、EF Core が未設定の判定に使う値も `0` のためです。以下は `HasDefaultValue(-1)` を指定し、センチネルを変更していない場合の例であり、すべての型や既定値の構成に当てはまる説明ではありません。
>
> 次は、`Count` に `HasDefaultValue(-1)` を設定して 3 行を挿入する確認例です。
>
> | 設定した内容 | 保存された値 |
> | --- | --- |
> | 何も設定しない | `-1` |
> | **`Count = 0` を明示** | **`-1`** |
> | `Count = 5` を明示 | `5` |
>
> 意図した `0` が黙って `-1` に化けます。EF Core 8.0 以降は、この「値が設定されたかどうか」の判定に使う基準値（**センチネル**）を `HasSentinel` で変更できます。センチネルを `-1` にすれば、`-1` のときだけデータベースの既定値が使われ、`0` は通常の値として挿入されます。
>
> ```csharp
> modelBuilder.Entity<Item>()
>     .Property(e => e.Count)
>     .HasDefaultValue(-1)
>     .HasSentinel(-1);
> ```
>
> 公式ドキュメントは、これをエンティティ型側にも反映することを勧めています。プロパティの初期化子でセンチネル値を設定しておくと、インスタンスを作った時点で「未設定」の状態から始まります。
>
> ```csharp
> public class Item
> {
>     public int Id { get; set; }
>     public int Count { get; set; } = -1;   // センチネル
> }
> ```
>
> 次は、未設定・明示的な `0`・明示的な `5` を挿入する確認例です。**`HasSentinel(-1)` の構成だけ `Count` の初期化子を `-1` とし、ほかの 2 構成では初期化子を付けず、CLR 既定値の `0` から開始しています。**
>
> | 設定した内容 | 既定値のみ | **`HasSentinel(-1)`** | `ValueGeneratedNever()` |
> | --- | --- | --- | --- |
> | 何も設定しない | `-1` | `-1` | **`0`** |
> | `Count = 0` を明示 | **`-1`** | **`0`** | **`0`** |
> | `Count = 5` を明示 | `5` | `5` | `5` |
>
> `ValueGeneratedNever()` は、公式が「マイグレーションが列を作るときの既定値制約は構成したいが、EF には常に値を挿入させたい場合」の方法として挙げているものです。ただし**未設定でもデータベースの既定値が使われなくなる**点に注意してください。上の実測でも、何も設定しない行が `-1` ではなく `0` になっています。
>
> プロパティを `int?` のような **null 許容型**にする方法も有効です。`int?` の CLR 既定値は `null` なので、未設定の `null` と明示的な `0` を区別できます。EF Core 10.0.11 / SQLite で `HasDefaultValue(-1)` を指定する確認例の保存値は、未設定が `-1`、明示的な `0` が `0`、明示的な `5` が `5` です。
>
> `bool` では、[EF Core 8 以降の公式説明](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-8.0/whatsnew#database-defaults-for-booleans)にあるとおり、定数のデータベース既定値に合わせてセンチネルが設定されます。EF Core 10.0.11 / SQLite の `HasDefaultValue(true)` と `HasDefaultValue(false)` の確認例でも、明示的な `false` と `true` の保存を確認できています。初期化子のないこの `bool` では、未設定時の保存値は CLR 既定値の `false` です。

> [!WARNING]
> 公式ドキュメントは「**既存のマイグレーションがある状態で `UseNamedDefaultConstraints()` を有効にすると、次に追加するマイグレーションでモデル内のすべての既定値制約がリネームされる**」と注意しています。稼働中のデータベースに対して有効化する場合は、生成されたマイグレーションの差分を必ず確認してください。

### EF Core ツールの使い分け

公式ドキュメントは「**ツールパッケージのバージョンは、常にランタイムパッケージのメジャーバージョンに合わせて使用する**」と明記しています。バージョンがずれていても即座に失敗するとは限らないため、気づかないまま運用してしまいがちです。

次は、EF Core 10.0.11 のランタイムに対して EF Core 9.0.0 の `dotnet-ef` で `migrations add` を実行する確認例の警告です。この条件で生成が成功していても、メジャーバージョンを合わせる公式の指針は変わりません。

```text
The Entity Framework tools version '9.0.0' is older than that of the runtime '10.0.11'.
Update the tools for the latest features and bug fixes.
```

「動いてしまう」ため放置されやすいのですが、古いツールは新しいバージョンで追加された機能や修正を知りません。この警告が出たら、そのままにせずツールを更新してください。

#### `database update <Name>` は「そこまで戻す」であって「1 つ戻す」ではない

`dotnet ef database update` にマイグレーション名を渡すと、**コマンド完了後にデータベースがその状態になる**ように動きます。公式ドキュメントは「データベースが指定より新しいマイグレーションにある場合、コマンドは対象より新しいマイグレーションを**すべて** `Down` の実行によって取り消す。古いマイグレーションを 1 つだけ順序外に適用するのではない」と警告しています。

次は、`M1` / `M2` / `M3` を適用した SQL Server 2022 で `M1` を指定する確認例です。2 件の取り消しを確認できています。

```text
Reverting migration '20260908035812_M3'.
Reverting migration '20260908035806_M2'.
Done.
```

```text
20260908035800_M1
20260908035806_M2 (Pending)
20260908035812_M3 (Pending)
```

「1 つだけ戻すつもりで古い名前を指定したら、間のマイグレーションまで全部 `Down` が走った」という事故になり得ます。`Down` はデータを失う操作を含むことがあるため、本番環境では実行前に必ず `dotnet ef migrations list` で現在地を確認してください。

---

#### DbContext が複数あるときは `--context` が必須

[公式の CLI オプションの説明](https://learn.microsoft.com/ja-jp/ef/core/cli/dotnet#common-options)では、コンテキストクラスが複数ある場合は `--context` が必須です。次は、2 つの `DbContext` があるプロジェクトで指定を省略したときの確認例です。

```text
More than one DbContext was found. Specify which one to use.
Use the '-Context' parameter for PowerShell commands and the '--context' parameter for dotnet commands.
```

`--context` の候補は `dotnet ef dbcontext list` で確認できます。

```bash
dotnet ef dbcontext list
# Db
# AuditDb
```

マイグレーションを追加するときは、`--context` に加えて `--output-dir` も指定してください。指定しないと、すべての `DbContext` のマイグレーションが同じ `Migrations` フォルダーに混在します。

```bash
dotnet ef migrations add InitialCreate --context AuditDb --output-dir AuditMigrations
```

`database update` や `migrations script` など、他のコマンドでも同様に `--context` が必要です。

---

Visual Studio では、`dotnet ef` の代わりにパッケージマネージャーコンソールから PowerShell コマンドを使えます。また、複数のターゲットフレームワークを持つプロジェクトでは `--framework` の指定が必要です。

<details>
<summary>Visual Studio のパッケージマネージャーコンソールを使う場合</summary>

Visual Studio では、`Microsoft.EntityFrameworkCore.Tools` パッケージを追加すると、パッケージマネージャーコンソールから PowerShell コマンドを使用できます。

```powershell
Install-Package Microsoft.EntityFrameworkCore.Tools
```

以降、本章で紹介する `dotnet ef` コマンドは、次のように読み替えられます。

| .NET CLI | パッケージマネージャーコンソール |
| --- | --- |
| `dotnet ef migrations add <Name>` | `Add-Migration <Name>` |
| `dotnet ef database update` | `Update-Database` |
| `dotnet ef migrations script` | `Script-Migration` |
| `dotnet ef migrations remove` | `Remove-Migration` |

</details>

マイグレーションを扱うコマンド以外にも、構成の確認やスキーマの出力に使えるサブコマンドがあります。

| コマンド | 用途 |
| --- | --- |
| `dotnet ef dbcontext info` | `DbContext` の型、プロバイダー、接続先を表示する |
| `dotnet ef dbcontext list` | プロジェクト内の `DbContext` を一覧表示する |
| `dotnet ef dbcontext script` | **マイグレーションを介さず**、モデルから直接 SQL スクリプトを生成する |

`dotnet ef dbcontext info` は、設計時にどの接続文字列が使われているかを確認したいときに便利です（実測で確認）。

```text
Type: Ctx
Provider name: Microsoft.EntityFrameworkCore.Sqlite
Database name: main
Data source: cli.db
Options: None
```

> [!TIP]
> すべての `dotnet ef` コマンドは、既定でプロジェクトをビルドしてから実行します。直前にビルド済みであることが分かっている場合は **`--no-build`** を付けると待ち時間を減らせます。公式ドキュメントも「ビルドが最新である場合に使うことを想定している」と説明しています。ソースを変更したあとに付けると古いモデルで実行されるため、CI では付けないでください。

> [!NOTE]
> `dotnet ef dbcontext script` が出力するのは、**現在のモデルからスキーマを作るための SQL** です。マイグレーションの履歴を考慮しないため、既存のデータベースに対する差分にはなりません。マイグレーションを使わずに `EnsureCreated()` で運用しているプロジェクトで、生成される DDL を事前に確認したいときに使えます。マイグレーションを使っている場合は `dotnet ef migrations script` のほうを使ってください。

> [!IMPORTANT]
> EF Core 10 では、プロジェクトが `<TargetFramework>` ではなく **`<TargetFrameworks>`（複数形）で複数のフレームワークを対象にしている場合、`--framework` オプションの指定が必須** になりました。指定しないと `dotnet ef` は次のエラーで停止します（実測で確認済み）。
>
> ```text
> The project targets multiple frameworks. Use the --framework option to specify which target framework to use.
> ```
>
> ライブラリプロジェクトに `DbContext` を置いていて複数ターゲットにしている場合など、EF Core 9 から移行すると CI が突然失敗します。次のようにフレームワークを明示してください。
>
> ```bash
> dotnet ef migrations add AddPostPublishedAt --framework net10.0
> dotnet ef database update --framework net10.0
> ```

### SQL スクリプトとマイグレーションバンドルを使い分ける

SQL スクリプトは、DBA によるレビューやアーカイブが必要な場合に適しています。

```bash
# 空のデータベースから最新までのスクリプトを生成
dotnet ef migrations script

# 冪等 (idempotent) なスクリプトをファイルに出力
dotnet ef migrations script --idempotent --output artifacts/migrations.sql

# 特定のマイグレーション間のスクリプトを生成
dotnet ef migrations script AddNewTables AddAuditTable
```

`--idempotent` を付けると、移行履歴を確認し、未適用のマイグレーションだけを実行するスクリプトが生成されます。同じ一連のマイグレーションで管理され、適用済みの位置が異なるデータベースに使えます。手作業によるスキーマ変更まで検出・修復する仕組みではないため、適用前の SQL レビューは必要です。

> [!WARNING]
> **SQLite ではべき等スクリプトを生成できません。** 公式ドキュメントは「他のデータベースと違い SQLite には手続き言語が含まれていないため、べき等スクリプトが必要とする if-then の論理を生成する方法がない」と説明しています。SQLite のプロジェクトの確認例でも、次のエラーを確認できています。
>
> ```text
> Generating idempotent scripts for migrations is not currently supported for SQLite.
> ```
>
> 公式は代わりに、最後に適用したマイグレーションが分かっているなら `dotnet ef migrations script <そのマイグレーション名>` で差分スクリプトを作り、分からないなら `dotnet ef database update --connection "Data Source=My.db"` で適用することを勧めています。テストで SQLite を使う場合（[付録 EF Core 6](../appendix-efcore-06/index.md)）は、この違いに注意してください。

> [!NOTE]
> SQLite は `ALTER TABLE` でできることが少ないため、EF Core は多くのスキーマ変更を**テーブルの再構築 (rebuild)** に置き換えます。次は、列を 1 つ削除するマイグレーションの SQLite 向け生成 SQL の確認例です。
>
> ```sql
> -- SQLite
> CREATE TABLE "ef_temp_Items" ( "Id" INTEGER NOT NULL CONSTRAINT "PK_Items" PRIMARY KEY AUTOINCREMENT, "Name" TEXT NOT NULL );
> INSERT INTO "ef_temp_Items" ("Id", "Name") SELECT "Id", "Name" FROM "Items";
> DROP TABLE "Items";
> ALTER TABLE "ef_temp_Items" RENAME TO "Items";
> ```
>
> 公式は「再構築が可能なのは EF Core のモデルに含まれるデータベースオブジェクトだけで、マイグレーションの中で手作業で作ったものなど、モデルに含まれないオブジェクトがあると `NotSupportedException` が投げられる」と注意しています。

自動デプロイには **マイグレーションバンドル** が推奨されます。公式ドキュメントによれば、バンドルは CI で生成でき、実行時に .NET SDK も EF Core ツールもアプリケーションのソースコードも不要で、**自己完結型にすれば .NET ランタイムすら不要**な単一の実行可能ファイルです。EF Core のマイグレーションロックも機能します。

```bash
# .NET ランタイムがインストール済みの環境向け
dotnet ef migrations bundle --output efbundle

# .NET ランタイムごと同梱する（Linux x64 向け）
dotnet ef migrations bundle --self-contained --target-runtime linux-x64 --output efbundle
```

生成したバンドルは、デプロイ先で実行可能ファイルとして起動します。

```bash
# デプロイ先で実行
./efbundle --connection "$CONNECTION_STRING"
```

> [!WARNING]
> **`DbContext` の構成が `appsettings.json` を読んでいる場合、その構成ファイルをバンドルと一緒にコピーしてください。** 公式ドキュメントは「構成ファイルはバンドルの実行ディレクトリから解決される」と述べています。`dotnet ef migrations bundle` 自身も、生成の最後に次のメッセージを出します（実測で確認）。
>
> ```text
> Don't forget to copy appsettings.json alongside your bundle if you need it to apply migrations.
> ```
>
> [公式のバンドル適用ガイド](https://learn.microsoft.com/ja-jp/ef/core/managing-schemas/migrations/applying#bundles)は、本番の秘密情報を構成ファイルに置かず、安全な構成ソースまたは `--connection` オプションで渡すよう案内しています。

> [!NOTE]
> 公式ドキュメントは、バンドルの制約として「SQL スクリプトと違い、実行される SQL を事前に確認したり、含まれるマイグレーションを一覧したりする手段が現時点ではない」と述べています。デプロイ前に SQL のレビューが必要な運用では、バンドルではなく `dotnet ef migrations script` でスクリプトを生成してください。

> [!TIP]
> EF Core 9 以降、マイグレーションの実行はデータベース全体のロックで保護されます（SQL スクリプトによる適用は除く）。これにより、複数のインスタンスが同時にマイグレーションを試みても、実行は 1 つに直列化されます。実際に SQL Server 2022 へ `dotnet ef database update` を実行すると、適用の前に次のメッセージが表示され、ロックの取得が行われていることが確認できます。
>
> ```text
> Acquiring an exclusive lock for migration application.
> See https://aka.ms/efcore-docs-migrations-lock for more information if this takes too long.
> ```

### マイグレーションバンドルの対象ランタイムを指定する

[公式のバンドル用オプション](https://learn.microsoft.com/ja-jp/ef/core/cli/dotnet#dotnet-ef-migrations-bundle)では、出力するバンドルの対象ランタイムは **`--target-runtime`（短縮形 `-r`）** で指定します。共通オプションの `--runtime` は、パッケージを復元する対象のランタイム識別子であり、バンドルの指定とは区別してください。

macOS から `--target-runtime linux-x64` を指定した確認例では、Linux 向けの実行可能ファイル（ELF 64-bit）の生成と、そのバンドルによる SQL Server 2022 へのマイグレーション適用を確認できています。

### モデルとマイグレーションのずれを検出する

エンティティを変更したのにマイグレーションを追加し忘れると、モデルとデータベースのスキーマがずれます。**EF Core 9 以降、この状態で `Migrate()` / `MigrateAsync()` または `dotnet ef database update` を呼ぶと例外になります。** 公式は影響度が「High」の破壊的変更として扱っています。

次は、`Blog` に `Url` を追加してマイグレーションを作らず、SQL Server 2022 に対して `MigrateAsync()` を呼ぶ確認例の例外です。

```text
InvalidOperationException: An error was generated for warning
'Microsoft.EntityFrameworkCore.Migrations.PendingModelChangesWarning':
The model for context 'PendContext' has pending changes.
Add a new migration before updating the database.
```

同じ状態で `dotnet ef database update` を実行した場合も、同じ警告 ID で失敗します。

#### コードから検出する

`DatabaseFacade.HasPendingModelChanges()` で同じ判定をコードから行えます。公式は「マイグレーションの追加を忘れたときに失敗する単体テストを書くのに使える」と述べています。

`connectionString` には本編で設定した SQL Server の接続文字列を渡します。本編の `BloggingContext` は引数なしでは作成できないため、`DbContextOptions<BloggingContext>` を明示して構成します。

```csharp
var options = new DbContextOptionsBuilder<BloggingContext>()
    .UseSqlServer(connectionString)
    .Options;
using var db = new BloggingContext(options);

if (db.Database.HasPendingModelChanges())
{
    throw new InvalidOperationException("マイグレーションが追加されていません。");
}
```

#### 未適用マイグレーションとモデル差分を区別する

`GetMigrations()` は構成されたマイグレーションアセンブリ内の一覧、`GetAppliedMigrationsAsync()` は対象データベースの適用済み一覧、`GetPendingMigrationsAsync()` はそのアセンブリ内で対象データベースへ未適用の一覧を返します。公式の[マイグレーションの一覧表示](https://learn.microsoft.com/ja-jp/ef/core/managing-schemas/migrations/managing#listing-migrations)には、コードから取得する例もあります。

```csharp
var allMigrations = db.Database.GetMigrations();
var appliedMigrations = await db.Database.GetAppliedMigrationsAsync();
var pendingMigrations = await db.Database.GetPendingMigrationsAsync();
```

次は、EF Core 10.0.11 / SQL Server 2022 でテーブル作成と列追加の 2 つのマイグレーションを使う確認例です。

| 状態 | 全件 | 適用済み | 未適用 | `HasPendingModelChanges()` |
| --- | ---: | ---: | ---: | --- |
| データベース未作成 | 2 | 0 | 2 | `false` |
| 最初の 1 件だけ適用 | 2 | 1 | 1 | `false` |
| 2 件とも適用 | 2 | 2 | 0 | `false` |
| さらにモデルだけ変更し、マイグレーションは未追加 | 2 | 2 | 0 | `true` |

**未適用が 0 件でも、マイグレーションの作成忘れがないとは限りません。** `GetPendingMigrationsAsync()` はまだマイグレーションになっていないモデル変更を検出しません。その判定には前述の `HasPendingModelChanges()` を使ってください。

#### CI で検出する

`dotnet ef migrations has-pending-model-changes` はモデル変更の追加忘れを検出するコマンドです。EF Core 10.0.11 の公式実装では、[変更があれば `OperationException` を発生させ](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore.Design/Design/Internal/MigrationsOperations.cs)、[CLI が例外を終了コード 1 として返します](https://github.com/dotnet/efcore/blob/v10.0.11/src/ef/Program.cs)。次の終了コードを確認できており、CI の判定に利用できます。終了コード 1 は他のエラーでも返るため、原因は出力メッセージで確認してください。

| 状態 | 出力 | 終了コード |
| --- | --- | --- |
| 保留中の変更あり | `Changes have been made to the model since the last migration. Add a new migration.` | 1 |
| 保留中の変更なし | `No changes have been made to the model since the last migration.` | 0 |

> [!WARNING]
> [公式のモデル変更検出の説明](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/breaking-changes#exception-is-thrown-when-applying-migrations-if-there-are-pending-model-changes)にあるとおり、この警告は `ConfigureWarnings` で抑制できます。ただし、追加し忘れたマイグレーションの代わりにはなりません。
>
> ```csharp
> options.UseSqlServer(connectionString)
>        .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
> ```
>
> **抑制してもモデルの変更は適用されません。** [EF Core 9 の変更履歴](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/breaking-changes#exception-is-thrown-when-applying-migrations-if-there-are-pending-model-changes)は、マイグレーションに含まれないモデル変更が `Migrate` では適用されないことを説明しています。上の条件で警告を抑制する確認例でも、`MigrateAsync()` は成功していますが、`Blogs` の列は `Id` と `Name` のままで `Url` は未作成です。モデル変更の追加忘れには、抑制ではなくマイグレーションの追加で対応してください。

### 同時にマイグレーションが走らないようにする

起動時マイグレーションでいちばん怖いのは、**複数のインスタンスが同時に起動してマイグレーションを二重に適用する**ことです。コンテナーをスケールアウトした瞬間や、ローリングデプロイの最中に起こります。

公式ドキュメントによれば、EF Core 9 以降の `Migrate()` / `MigrateAsync()` は、マイグレーションを適用する前に**データベース全体のロックを自動で取得**します。ロックはマイグレーションの実行中（シードコードの実行も含む）保持され、完了すると自動的に解放されます。ロックは `dotnet ef database update`、`Update-Database`、マイグレーションバンドル、実行時マイグレーションのいずれにも適用されます。SQL スクリプトは EF Core の外で適用されるため対象外です。

SQL Server ではセッションレベルのアプリケーションロックが使われます。次は、`MigrateAsync()` のログで確認できている取得・解放の SQL です。

```sql
DECLARE @result int;
EXEC @result = sp_getapplock @Resource = '__EFMigrationsLock', @LockOwner = 'Session', @LockMode = 'Exclusive';
SELECT @result

-- （マイグレーション適用後）
DECLARE @result int;
EXEC @result = sp_releaseapplock @Resource = '__EFMigrationsLock', @LockOwner = 'Session';
SELECT @result
```

次は、別の接続で同じリソース名の `sp_getapplock` を先に取得し、`MigrateAsync()` の開始から 6 秒後に解放する確認例です。

```text
先行ロック取得: 戻り値 0
6.0 秒後にロックを解放した
MigrateAsync 完了まで 6.1 秒（待たされた）
```

この確認例では、`MigrateAsync()` がロックの解放まで待機し、解放後に処理を継続することを確認できています。ロック待機の確認であり、あらゆる運用条件で二重適用が起きないことを実測で証明したものではありません。

#### マイグレーションを自分でトランザクションに包んではいけない

[公式のマイグレーション適用ガイド](https://learn.microsoft.com/ja-jp/ef/core/managing-schemas/migrations/applying#migration-locking)は、明示的トランザクションでの `MigrateAsync()` のラップをサポートしていません。次は、避けるべき書き方です。

```csharp
// これは公式にサポートされない書き方
await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
{
    await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
    await db.Database.MigrateAsync(cancellationToken);
    await tx.CommitAsync(cancellationToken);
});
```

**公式ドキュメントは、`MigrateAsync` を明示的トランザクションで包むことを「サポートされない」と明記しています。** 理由は、外側でトランザクションを開始してしまうと**上で説明したデータベースロックを取得できなくなり、同時実行から保護されなくなる**ためです。EF Core 9 以降の `Migrate` / `MigrateAsync` は、必要なトランザクションと実行戦略を自分で管理します。

EF Core 9 ではこのパターンが `MigrationsUserTransactionWarning` による例外になりました。EF Core 10.0.11 の上のコードの確認例では、例外ではなく同じ警告 ID のログを確認できています。**成功観測を、公式にサポートされる使い方と解釈しないでください。**

```text
warn: RelationalEventId.MigrationsUserTransactionWarning[20412] (Microsoft.EntityFrameworkCore.Migrations)
```

例外にならなくても、ロックによる保護が失われることに変わりはありません。**外側のトランザクションと実行戦略は外して、`MigrateAsync()` をそのまま呼んでください。**

```csharp
await db.Database.MigrateAsync(cancellationToken);
```

> [!NOTE]
> EF Core 9 では、保留中のマイグレーションを**すべて 1 つのトランザクション**にまとめて適用する変更が入りましたが、公式の EF Core 10 のリリースノートによれば、この変更は「さまざまなマイグレーションのシナリオで問題を起こした」として **EF Core 10 で元に戻されました**。EF Core 10 では以前と同じく、マイグレーションごとにトランザクションが張られます。

#### SQLite では放置されたロックが残る

SQLite にはアプリケーションロックの仕組みがないため、公式ドキュメントによれば EF Core は代わりに **`__EFMigrationsLock` テーブル**を作成し、そこに行を挿入することでロックを表現します。次は、マイグレーション適用後のテーブル一覧の確認例です。

```text
テーブル: __EFMigrationsLock, __EFMigrationsHistory
```

公式ドキュメントは、**マイグレーションの異常終了でロックが残り、後続の移行が解除を無期限に待つ可能性**を説明しています。次はロック行を残した状態を用意して、後続のマイグレーションの待機を確認する例です。

```text
放置されたロック行を挿入した
8 秒経ってもロック待ちのまま
ロックテーブルを削除したら完了した
```

この確認例では、**8 秒後も待機し、残留ロックの除去後に完了する**ことを確認できています。無期限に観測した結果ではありません。公式ドキュメントが解決方法に挙げる `__EFMigrationsLock` テーブルの削除は、次の SQL です。

```sql
DROP TABLE "__EFMigrationsLock";
```

> [!WARNING]
> 公式ドキュメントは「ロックの仕組みはプロバイダーによって大きく異なり、プロバイダー固有の問題を伴うことがある」と明記しています。使用するプロバイダーのドキュメントを必ず確認してください。
>
> また、`MigrateAsync()` を**明示的なトランザクションで囲むことはサポートされていません**。マイグレーションのトランザクションは EF Core 自身が管理します。

### 特定のテーブルをマイグレーションの対象から外す

同じエンティティ型を複数の `DbContext` にマップしたい場面があります。境界づけられたコンテキスト (Bounded Context) ごとに `DbContext` を分ける設計では、片方のコンテキストがテーブルを所有し、もう片方は読み取りのために同じテーブルを参照するといった構成になります。このとき両方のコンテキストでマイグレーションを作ると、同じテーブルを二重に作ろうとして衝突します。

`ExcludeFromMigrations` を使うと、モデルには含めたままマイグレーションの対象からだけ外せます。

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<AuditLog>()
        .ToTable("AuditLogs", t => t.ExcludeFromMigrations());
}
```

上のモデルの `GenerateCreateScript()` の確認例では、`Products` の `CREATE TABLE` のみが生成され、`AuditLogs` の DDL は含まれていません。一方、エンティティ型はモデルに残るため、`db.AuditLogs` に対するクエリでは `SELECT COUNT(*) FROM [AuditLogs]` の発行を確認できています。

> [!WARNING]
> **除外するのは、このコンテキストによるスキーマ管理の対象です。構成や DDL 生成でテーブルの実在を確認するわけではありません。** テーブルを作らずに問い合わせる確認例では、`SqlException: Invalid object name 'AuditLogs'.` を確認できています。クエリの実行前に、他のコンテキストのマイグレーションなどでテーブルを用意してください。

> [!TIP]
> 再びマイグレーションで管理したくなったら、`ExcludeFromMigrations` を外した状態で新しいマイグレーションを作成します。それ以降の変更はマイグレーションに含まれるようになります。

### マイグレーション履歴テーブルをカスタマイズする

EF Core は適用済みのマイグレーションを `__EFMigrationsHistory` テーブルに記録します。このテーブルの名前・スキーマ・列名は変更できます。既存のデータベースの命名規則に合わせたい場合や、アプリケーション用のテーブルと分けて管理したい場合に使います。

テーブル名とスキーマは `MigrationsHistoryTable()` で指定します。

```csharp
options.UseSqlServer(
    connectionString,
    x => x.MigrationsHistoryTable("__MyMigrationsHistory", "mySchema"));
```

列名など、それ以上の構成を変えるには、プロバイダー固有の `IHistoryRepository` サービスを差し替えます。次は SQL Server で `MigrationId` 列の名前を `Id` に変える例です。

```csharp
#pragma warning disable EF1001

internal class MyHistoryRepository : SqlServerHistoryRepository
{
    public MyHistoryRepository(HistoryRepositoryDependencies dependencies)
        : base(dependencies)
    {
    }

    protected override void ConfigureTable(EntityTypeBuilder<HistoryRow> history)
    {
        base.ConfigureTable(history);
        history.Property(h => h.MigrationId).HasColumnName("Id");
    }
}
```

```csharp
options
    .UseSqlServer(connectionString)
    .ReplaceService<IHistoryRepository, MyHistoryRepository>();
```

次は、両方の構成を適用するマイグレーションの、履歴テーブル作成 SQL の確認例です。

```sql
-- SQL Server
IF OBJECT_ID(N'[mySchema].[__MyMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'mySchema') IS NULL EXEC(N'CREATE SCHEMA [mySchema];');
    CREATE TABLE [mySchema].[__MyMigrationsHistory] (
        [Id] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___MyMigrationsHistory] PRIMARY KEY ([Id])
    );
END;
```

SQL Server 2022 への `dotnet ef database update` の確認例では、`mySchema.__MyMigrationsHistory` と、その `Id` / `ProductVersion` の 2 列を確認できています。

> [!IMPORTANT]
> 公式ドキュメントは「マイグレーションを**適用したあとで**履歴テーブルをカスタマイズした場合、データベース側の既存テーブルを更新するのは自分の責任である」と述べています。EF Core は古い `__EFMigrationsHistory` を新しい名前に移行してくれません。カスタマイズは最初のマイグレーションを適用する前に決めてください。

> [!WARNING]
> `SqlServerHistoryRepository` は**内部 (internal) 名前空間にあり、将来のリリースで変更される可能性がある**と公式が明記しています。継承するとビルド時に `EF1001` の警告が出るため、`#pragma warning disable EF1001` で抑制する必要があります。この警告は「使ってはいけない」という意味ではなく、「EF Core のバージョンを上げたときに壊れる可能性を受け入れているか」を確認するものです。テーブル名とスキーマの変更だけで足りるなら、`MigrationsHistoryTable()` にとどめてください。

---

### 初期データの投入（シード）

マスターデータや動作確認用の初期データを投入する方法は 2 つあります。

1 つ目は `HasData` です。モデルの一部として宣言し、マイグレーションの中に `InsertData` として埋め込まれます。主キーを明示的に指定する必要があります。

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Blog>().HasData(
        new Blog { Id = 1, Name = "既定のブログ", Url = "https://example.com" });
}
```

`HasData` はマイグレーションの差分計算に組み込まれるため、値を書き換えると次のマイグレーションで `UpdateData` が生成されます。一方で、実行時にしか決まらない値（現在時刻や外部 API から取得する値）は扱えません。

2 つ目は EF Core 9 で追加された `UseSeeding` / `UseAsyncSeeding` です。こちらは通常の `DbContext` 操作としてデータを投入するため、任意のロジックを書けます。`MigrateAsync()` や `EnsureCreatedAsync()` の実行時に呼び出されます。

```csharp
builder.Services.AddDbContext<BloggingContext>(options =>
    options.UseSqlServer(connectionString)
           .UseSeeding((context, _) =>
           {
               if (!context.Set<Blog>().Any(b => b.Name == "既定のブログ"))
               {
                  context.Set<Blog>().Add(new Blog
                  {
                      Name = "既定のブログ",
                      Url = "https://example.com"
                  });
                  context.SaveChanges();
              }
           })
           .UseAsyncSeeding(async (context, _, cancellationToken) =>
           {
              if (!await context.Set<Blog>()
                      .AnyAsync(b => b.Name == "既定のブログ", cancellationToken))
              {
                  context.Set<Blog>().Add(new Blog
                  {
                      Name = "既定のブログ",
                      Url = "https://example.com"
                  });
                  await context.SaveChangesAsync(cancellationToken);
              }
           }));
```

> [!IMPORTANT]
> [公式のシードデータの説明](https://learn.microsoft.com/ja-jp/ef/core/modeling/data-seeding#deployment-behavior)では、同期 API とツールは `UseSeeding`、非同期 API は `UseAsyncSeeding` を呼びます。**アプリケーションが非同期 API を使う場合も、ツール用に `UseSeeding` を実装してください。** この例は両方を登録しており、SQLite での `EnsureCreated()` / `EnsureCreatedAsync()` でも、それぞれのデリゲートが呼ばれることを確認できています。

> [!WARNING]
> **シードコードはロールバック（ダウングレード）のあとにも実行されます。** 公式ドキュメントは「構成されたシードコードはダウングレードのあとに実行される。ターゲットが `0` の場合にアプリケーションのスキーマが存在しないことも含め、ターゲットのマイグレーション時点のスキーマに耐えられなければならない」と述べています。
>
> 次は、SQLite の `dotnet ef database update 0` によりテーブルを削除した後、シードコード内で問い合わせる確認例の例外です。パイプ経由で取得したログであり、CLI 単体の終了コードの測定ではありません。
>
> ```text
> An exception occurred while iterating over the results of a query for context type 'Ctx'.
> Microsoft.Data.Sqlite.SqliteException (0x80004005): SQLite Error 1: 'no such table: Blogs'.
> ```
>
> シードコードは、移行先のスキーマに対象テーブルがない場合にも対応させてください。**すべての例外を握りつぶすのではなく**、想定したスキーマ未作成の場合と、それ以外の接続・保存の失敗を区別してください。
>
> また、これらのデリゲートは毎回の実行で呼ばれる可能性があるため、上記のように **既に存在するかを確認してから追加** してください。この点は `HasData` と異なり、EF Core が重複を防いでくれません。

### 既存データベースからスキャフォールディングする

既存のデータベースからエンティティと `DbContext` を生成する `dotnet ef dbcontext scaffold` には、そのまま使うと危険な既定の挙動があります。

このコマンドをそのまま実行すると、**生成された `DbContext` の `OnConfiguring` に接続文字列が埋め込まれます**。次は、SQL Server 2022 への接続文字列を直接渡した確認例で、生成コードとともに確認できている `#warning` です（接続先を伏せ、警告文の先頭部分のみ掲載）。

```csharp
protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code.
    => optionsBuilder.UseSqlServer("Server=...;User Id=sa;Password=...");
```

公式ドキュメントは、これは「生成されたコードが最初に使うときにいきなり動かないという体験を避けるため」であり、**接続文字列が製品コードに存在してはならない**と明記しています。`--no-onconfiguring` は `OnConfiguring` の生成を抑止します。この確認例でも、生成ファイルに `OnConfiguring` と接続文字列が含まれないことを確認できています。

```bash
dotnet ef dbcontext scaffold "<接続文字列>" Microsoft.EntityFrameworkCore.SqlServer \
    --output-dir Models --no-onconfiguring
```

#### 既定値を持つ `bool` 列は `bool?` にならない

データベースファーストで運用していると、EF Core 8.0 での変更が生成結果に効いてきます。**既定値の制約を持つ NULL 非許容の `bit` 列は、以前は `bool?` として生成されていましたが、EF Core 8.0 以降は `bool` として生成されます。**

次は、SQL Server 2022 の以下のテーブルをスキャフォールディングする確認例です。

```sql
-- SQL Server
CREATE TABLE Articles (
  Id int IDENTITY PRIMARY KEY,
  Title nvarchar(200) NOT NULL,
  IsPublished bit NOT NULL CONSTRAINT DF_Articles_IsPublished DEFAULT 1
);
```

この確認例で生成されるプロパティと構成は、次のとおりです。

```csharp
public bool IsPublished { get; set; }   // bool? ではない

entity.Property(e => e.IsPublished).HasDefaultValue(true, "DF_Articles_IsPublished");
```

以前 `bool?` にしていたのは、**`bool` の CLR 既定値が `false` のため、`false` を設定しても「未設定」と区別できず、データベースの既定値 `true` が入ってしまう**問題があったからです。EF Core 8.0 では、値が設定済みかどうかを判定する基準値（**センチネル**）を変更できるようになり、既定値が `true` の `bool` プロパティにはこれが自動で適用されます。このモデルの確認例でも `Sentinel = True` です。

このモデルの確認例では、`false` と `true` で発行される INSERT は次のとおりです。

```sql
-- SQL Server：IsPublished = false を設定した場合（値が送られる）
INSERT INTO [Articles] ([IsPublished], [Title])
OUTPUT INSERTED.[Id]
VALUES (@p0, @p1);

-- SQL Server：IsPublished = true を設定した場合（列が外れ、DB の既定値が使われて読み戻される）
INSERT INTO [Articles] ([Title])
OUTPUT INSERTED.[Id], INSERTED.[IsPublished]
VALUES (@p2);
```

> [!NOTE]
> 公式ドキュメントは、この変更が影響するのは「定期的にデータベースを再スキャフォールディングする データベースファーストのフロー」だけだと述べています。推奨される対応は、コード側を NULL 非許容の `bool` に合わせることです。どうしても以前の生成結果が必要な場合は、スキャフォールディングの T4 テンプレートを編集して元のマッピングに戻せます。

> [!WARNING]
> EF Core 8.0 では**生成されるナビゲーションの名前も変わることがあります**。以前は複合外部キーの列名に共通の接頭辞があるとそれをナビゲーション名に使っていましたが、この規則は廃止されました。公式は、この規則が `S` や `Student_`、ときには `_` だけといった「非常に貧弱な名前」を生むことがあったためだと説明しています。再スキャフォールディングでナビゲーション名が変わると既存のコードが壊れるため、差分を確認してください。

### 設計時 DbContext ファクトリ

`dotnet ef` コマンドは、設計時に `DbContext` のインスタンスを生成する必要があります。通常はアプリケーションの `Program.cs` からホストを構築して解決しますが、それが難しい構成（クラスライブラリにマイグレーションを置く場合など）では `IDesignTimeDbContextFactory<T>` を実装します。

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BloggingApi.Data;

public class BloggingContextFactory : IDesignTimeDbContextFactory<BloggingContext>
{
    public BloggingContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BloggingContext>();
        optionsBuilder.UseSqlServer(
            Environment.GetEnvironmentVariable("BLOGGING_CONNECTION")
                ?? @"Server=(localdb)\mssqllocaldb;Database=Blogging;Trusted_Connection=True");

        return new BloggingContext(optionsBuilder.Options);
    }
}
```

マイグレーションを別プロジェクトに置く場合は、コマンドで対象を指定します。ここで指定する 2 つのオプションは役割が異なります。公式ドキュメントによれば、`--project`（対象プロジェクト）は**生成されたファイルを受け取るプロジェクト**、`--startup-project`（スタートアッププロジェクト）は**ツールがビルドして実行するプロジェクト**です。ツールは接続文字列やモデルの構成を得るために、設計時にアプリケーションのコードを実行する必要があるためです。

```bash
dotnet ef migrations add InitialCreate \
    --project BloggingApi.Data \
    --startup-project BloggingApi.Data
```

> [!WARNING]
> [公式の別プロジェクト構成](https://learn.microsoft.com/ja-jp/ef/core/managing-schemas/migrations/projects)では、`Microsoft.EntityFrameworkCore.Design` を持つマイグレーションプロジェクトをスタートアップとして使います。**`--startup-project` に Web アプリケーションを指定する場合は、そちらにも設計時パッケージを用意してください。** 本節の構成で Web アプリケーション側へパッケージを追加しない対照では、次のエラーを確認できています。
>
> ```text
> Your startup project 'BloggingApi' doesn't reference Microsoft.EntityFrameworkCore.Design.
> This package is required for the Entity Framework Core Tools to work.
> Ensure your startup project is correct, install the package, and try again.
> ```
>
> `Microsoft.EntityFrameworkCore.Design` は `<PrivateAssets>all</PrivateAssets>` 付きで追加されるため、**プロジェクト参照をたどって伝播しません**。クラスライブラリに入れても、スタートアッププロジェクト側には効きません。
>
> 公式ドキュメントは、この構成では**マイグレーションを持つプロジェクトを対象とスタートアップの両方に指定する**ことを推奨しています。そうすればツールがアプリケーションの起動コードを実行せずに済み、Web アプリケーション側に設計時パッケージを追加する必要もなくなります。上のコマンド例が両方を `BloggingApi.Data` にしているのはこのためです。

> [!TIP]
> マイグレーションを `DbContext` と**同じアセンブリ**に置く場合は、マイグレーションアセンブリを別途指定する必要はありません。この構成での実行時の自動検出も確認できています。
>
> 一方、`DbContext` とマイグレーションを**別々のプロジェクト**に分ける場合は、[公式の構成手順](https://learn.microsoft.com/ja-jp/ef/core/managing-schemas/migrations/projects#configure-the-projects)に従い、マイグレーションアセンブリを指定します。本節の構成で指定を省く対照では、次のエラーを確認できています。
>
> ```text
> Your target project 'BloggingApi.Migrations' doesn't match your migrations assembly 'BloggingApi.Data'.
> Either change your target project or change your migrations assembly.
> ```
>
> このときは、実行時と設計時の両方で次のように構成します。
>
> ```csharp
> options.UseSqlServer(
>     connectionString,
>     b => b.MigrationsAssembly("BloggingApi.Migrations"));
> ```
>
> なお公式ドキュメントは、**データプロジェクトからマイグレーションプロジェクトを参照してはならない**と明記しています。マイグレーションプロジェクトがすでにデータプロジェクトを参照しているため、循環参照になるからです。

> [!NOTE]
> EF Core では、モデルの変更をもとにマイグレーションファイルを生成します。生成されたコードはそのまま適用せず、データを失う操作がないか確認してください。

---

#### 複数のフレームワークを対象にしているプロジェクト

`TargetFrameworks` で複数のフレームワークを対象にしているプロジェクトでは、**EF Core 10 からどのフレームワークを使うかの指定が必須になりました。**

```text
The project targets multiple frameworks.
Use the --framework option to specify which target framework to use.
```

```bash
dotnet ef migrations add Init --framework net10.0
```

[公式の EF Core 10 の変更説明](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-10.0/breaking-changes#ef-tools-now-require-framework-to-be-specified-for-multi-targeted-projects)に従い、複数のフレームワークを対象にする場合は実行対象を明示します。ライブラリプロジェクトで複数フレームワークを対象にしている場合は、CI のスクリプトにも `--framework` を追加してください。

#### 生成されるナビゲーション名は EF Core 8 で変わった

既存のデータベースから生成されるナビゲーションの名前は、EF Core 8 で変わりました。以前は**複合外部キーの列名に共通の接頭辞があると、そこから名前を作る**ことがあり、`S` や `Student_`、ひどいときは `_` だけという名前が生成されていました。

現在はこの規則が廃止されています。`Students` の複合主キーを `Sup_Id1` / `Sup_Id2` で参照する `Enrollments` の確認例でも、生成されるナビゲーション名は接頭辞の `Sup` ではなく、参照先の型名に基づく `Student` です。

```csharp
// Enrollment.cs
public virtual Student Student { get; set; } = null!;

// Student.cs
public virtual ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
```

> [!WARNING]
> **複合外部キー**のナビゲーション名の付け方は EF Core 8 で変わりました。公式ドキュメントは、以前の共通プレフィックスの規則が `S` や `_` のような名前を生むことがあったため廃止したと説明しています。`ProfFirstId` / `ProfSecondId` で `Students` を参照する `Courses` の比較例では、EF Core 7 の生成結果は `public virtual Student Prof`、EF Core 10 は `public virtual Student Student` です。再スキャフォールディング時は差分を確認してください。細かい命名制御には、公式が案内する T4 テンプレートのカスタマイズを検討します。

## 2. クエリの制御

### 単一クエリと分割クエリ

同じ階層の複数のコレクションナビゲーションを `Include` すると、EF Core は既定で 1 つの SQL に JOIN してまとめます。このとき、兄弟コレクションの行の組み合わせが増える **カルテシアン爆発 (Cartesian Explosion)** が起こります。転送量そのものを減らす手段は[付録5の「インデックスを正しく張る」](../appendix-efcore-05/index.md#インデックスを正しく張る)ではなく投影であり、判断の順序は[付録5の「まず計測する」](../appendix-efcore-05/index.md#まず計測する)に従ってください。

```csharp
var blogs = await context.Blogs
    .Include(b => b.Posts)
    .Include(b => b.Contributors)
    .ToListAsync(cancellationToken);
```

Post が 10 件、Contributor が 10 件あるブログでは、JOIN の結果は 100 行になり、ブログの列がすべての行に重複して転送されます。

これを避けるには `AsSplitQuery()` を使い、コレクションごとに別々のクエリを発行させます。

```csharp
var blogs = await context.Blogs
    .Include(b => b.Posts)
    .Include(b => b.Contributors)
    .AsSplitQuery()
    .ToListAsync(cancellationToken);
```

アプリケーション全体の既定を分割クエリにすることもできます。

```csharp
builder.Services.AddDbContext<BloggingContext>(options =>
    options.UseSqlServer(connectionString,
        sqlOptions => sqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
```

個別のクエリで単一クエリに戻したい場合は `AsSingleQuery()` を呼びます。

| 観点 | 単一クエリ（既定） | 分割クエリ |
| --- | --- | --- |
| ラウンドトリップ数 | 1 回 | コレクションの数だけ増える |
| データ転送量 | 重複により増える可能性がある | 兄弟コレクション間の行の積を避けられる。すべての列の重複がなくなるわけではない |
| 整合性 | データベースの単一クエリと分離レベルの保証範囲に従う | クエリ間で他のトランザクションによる変更が入り得る |

> [!WARNING]
> 分割クエリは既定では 1 つのトランザクションで実行されないため、クエリの合間に他のトランザクションがデータを変更すると、整合性のない結果になる可能性があります。公式は対策として、直列化可能またはスナップショット分離のトランザクションで包む方法を挙げています。単に既定の分離レベルでトランザクションを開始するだけの説明とは区別してください。なお 1 対 1 の関連エンティティは、公式が説明する分割クエリでも同じクエリの JOIN で読み込まれます。

> [!NOTE]
> [公式の分割クエリの説明](https://learn.microsoft.com/ja-jp/ef/core/querying/single-split-queries#split-queries)は、EF Core 10 より前に `Skip` / `Take` を使う場合、一意でない並び順では各クエリが異なる行を取得し、誤った結果となる可能性を警告しています。以下は、EF Core 10 での並べ替えの確認例です。
>
> 次は、EF Core 10 と SQL Server で以下のクエリを実行する確認例です。発行される 2 本の SQL を示します。
>
> ```csharp
> var blogs = await context.Blogs
>     .AsSplitQuery()
>     .Include(b => b.Posts)
>     .OrderBy(b => b.Name)
>     .Take(2)
>     .ToListAsync();
> ```
>
> ```sql
> SELECT TOP(@p) [b].[Id], [b].[Name]
> FROM [Blogs] AS [b]
> ORDER BY [b].[Name], [b].[Id]
>
> SELECT [p].[Id], [p].[BlogId], [p].[Title], [b0].[Id]
> FROM (
>     SELECT TOP(@p) [b].[Id], [b].[Name]
>     FROM [Blogs] AS [b]
>     ORDER BY [b].[Name], [b].[Id]
> ) AS [b0]
> INNER JOIN [Post] AS [p] ON [b0].[Id] = [p].[BlogId]
> ORDER BY [b0].[Name], [b0].[Id]
> ```
>
> [公式の分割クエリの注意事項](https://learn.microsoft.com/ja-jp/ef/core/querying/single-split-queries#split-queries)は、EF Core 10 より前の `Take` / `Skip` では一意な並び順を明示するよう求めています。上の EF Core 10 の確認例では、2 本目のサブクエリにも `ORDER BY [b].[Name], [b].[Id]` が含まれています。これは生成 SQL の確認であり、旧バージョンとの同時比較ではありません。**クエリ間のデータ変更まで防ぐものではない**点にも注意してください。

### LeftJoin / RightJoin 演算子

.NET 10 では LINQ に `LeftJoin` と `RightJoin` の演算子が追加され、[EF Core 10 はこれを SQL の `LEFT JOIN` / `RIGHT JOIN` に変換します](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-10.0/whatsnew#support-for-the-net-10-leftjoin-and-rightjoin-operators)。

```csharp
var results = await context.Students
    .LeftJoin(
        context.Departments,
        student => student.DepartmentId,
        department => department.Id,
        (student, department) => new
        {
            student.Name,
            DepartmentName = department != null ? department.Name : "所属なし"
        })
    .ToListAsync(cancellationToken);
```

従来は `SelectMany` と `GroupJoin`、`DefaultIfEmpty` を組み合わせる必要がありましたが、意図が明確に表現できるようになりました。

### 大文字小文字の区別は照合順序が決める

C# の `==` は大文字小文字を区別しますが、**SQL に翻訳された後は、データベースの照合順序 (collation) が区別するかどうかを決めます。** 次のクエリの確認例では、大文字小文字を区別しない照合順序で `John` と `JOHN` の両方への一致を確認できています。

```csharp
var count = await context.Customers.Where(c => c.Name == "john").CountAsync(cancellationToken);
// SQL: WHERE [c].[Name] = N'john'  →  実測で 2 件（John と JOHN）
```

EF Core は `==` を単に SQL の `=` に翻訳するだけで、大文字小文字の扱いを揃えようとはしません。これは意図的な設計です。そのため、`StringComparison` を受け取るオーバーロードは**翻訳できず例外になります。**

```csharp
// C# としてはコンパイルできる。Where だけではまだ SQL に翻訳されない
var query = context.Customers
    .Where(c => c.Name.Equals("john", StringComparison.OrdinalIgnoreCase));

// ここで SQL 翻訳が行われ、InvalidOperationException になる
await query.ToListAsync(cancellationToken);
```

クエリ単位で照合順序を指定したい場合は `EF.Functions.Collate` を使います。

```csharp
var exact = await context.Customers
    .Where(c => EF.Functions.Collate(c.Name, "SQL_Latin1_General_CP1_CS_AS") == "John")
    .CountAsync(cancellationToken);
// SQL: WHERE [c].[Name] COLLATE SQL_Latin1_General_CP1_CS_AS = N'John'  →  実測で 1 件
```

#### 照合順序の上書きとインデックスへの影響

[公式の照合順序とインデックスの説明](https://learn.microsoft.com/ja-jp/ef/core/miscellaneous/collations-and-case-sensitivity#explicit-collations-and-indexes)では、インデックスは列の照合順序を引き継ぎ、**クエリで別の照合順序を指定すると、その利用を妨げる可能性があります**。次は、`Name` に非クラスター化インデックスを持つ 2,001 行のテーブルで、SQL Server 2022 の [`SET SHOWPLAN_ALL ON`](https://learn.microsoft.com/ja-jp/sql/t-sql/statements/set-showplan-all-transact-sql?view=sql-server-ver16) により取得した推定プランの確認例です。実行時間や実際の実行プランの測定ではありません。

| クエリ | 実行プラン |
| --- | --- |
| `WHERE [Name] = N'John'` | `Index Seek(OBJECT:(...[IX_Customers_Name]))` |
| `WHERE [Name] COLLATE SQL_Latin1_General_CP1_CS_AS = N'John'` | `Clustered Index Scan(OBJECT:(...[PK_Customers]))` |
| `WHERE LOWER([Name]) = N'john'` | `Clustered Index Scan(OBJECT:(...[PK_Customers]))` |

[公式の照合順序の説明](https://learn.microsoft.com/ja-jp/ef/core/miscellaneous/collations-and-case-sensitivity#explicit-collations-and-indexes)は、`EF.Functions.Collate` や `string.ToLower` がインデックスの利用を妨げる可能性を警告しています。上の条件ではシークとスキャンの推定プランの違いを確認できていますが、処理時間の差や、行数を増やしたときの性能を示す測定ではありません。

> [!WARNING]
> 大文字小文字の区別を変えたいなら、**クエリではなく列またはデータベースの照合順序として定義してください。** そうすればすべてのクエリが暗黙にその照合順序を使い、インデックスの恩恵も受けられます。公式も「大量のデータを扱う性能上重要なクエリでは、必ず実行プランを確認し、適切なインデックスが使われているか確かめること」と警告しています。

```csharp
// 列の照合順序として定義する（この列に対するすべてのクエリに適用される）
modelBuilder.Entity<Customer>()
    .Property(c => c.Name)
    .UseCollation("SQL_Latin1_General_CP1_CS_AS");
```

#### bool?・int? の null を文字列に変換する

EF Core 9 の[変更内容](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/breaking-changes#tostring-method-now-returns-empty-string-for-null-instances)は、null 許容値型の `ToString()` の翻訳を C# の動作に合わせ、**null の場合は空文字列を返す**よう変更したと説明しています。以下は、SQL Server プロバイダー / EF Core 10.0.11 の `bool?` と `int?` 列の確認例です。

```csharp
var q = db.Things.Select(x => new { F = x.Flag.ToString(), N = x.Num.ToString() });
```

この確認例では、`bool?` と `int?` が `null` の行の結果は空文字列です。生成 SQL は次のとおりです。

```sql
SELECT CASE [t].[Flag]
    WHEN CAST(0 AS bit) THEN N'False'
    WHEN CAST(1 AS bit) THEN N'True'
    ELSE N'' END AS [F],
    COALESCE(CONVERT(varchar(11), [t].[Num]), '') AS [N]
FROM [Things] AS [t]
```

```text
Flag='' (null? False)  Num='' (null? False)
Flag='True'            Num='5'
```

> [!WARNING]
> **null 許容値型と `string?` を混同しないでください。** [SQL Server の公式実装](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore.SqlServer/Query/Internal/Translators/SqlServerObjectToStringTranslator.cs#L75-L78)と[SQLite の公式実装](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore.Sqlite.Core/Query/Internal/Translators/SqliteObjectToStringTranslator.cs#L68-L71)は、文字列型の `ToString()` では式をそのまま返します。両プロバイダーでの `string?` 列の確認例でも、元の値が `null` なら結果は `null` です。これを C# で直接実行する場合は別で、null の文字列参照への `ToString()` 呼び出しは `NullReferenceException` になります。

**返ってくるのは `null` ではなく空文字列**である点に注意してください。`== null` での判定は成立しません。以前の動作に戻したい場合は、公式ドキュメントが示すとおりクエリを書き換えます。

```csharp
db.Things.Select(x => x.Flag == null ? null : x.Flag.ToString());
```

#### 文字列の主キーは大文字小文字を区別せずに比較される

照合順序の話にはもう 1 つ落とし穴があります。EF Core 8 以降、SQL Server / Azure SQL プロバイダーでは、**文字列の主キーや外部キーの値を .NET 側でも大文字小文字を区別せずに比較します**。データベース側の既定の照合順序に合わせるための変更です。

つまり、`abc` を追跡している状態で `ABC` という別のインスタンスを追跡させようとすると、**同じキーとみなされて失敗します**。

```csharp
db.Attach(new Customer { Id = "abc", Name = "小文字" });
db.Attach(new Customer { Id = "ABC", Name = "大文字" }); // ここで例外
```

```text
InvalidOperationException: The instance of entity type 'Customer' cannot be tracked
because another instance with the same key value for {'Id'} is already being tracked.
```

この例でも、データベースへの問い合わせ前の **変更追跡の段階での例外**を確認できています。

大文字小文字を区別したいのであれば、[公式の移行手順](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-8.0/breaking-changes#sql-server-key-values-are-compared-case-insensitively)が示すとおり、キーのプロパティに、変更追跡で使う **`ValueComparer`（値比較子）** を `SetValueComparer` で明示的に構成します。プロバイダー値の比較に使う `SetProviderValueComparer` とは区別してください。

```csharp
modelBuilder.Entity<Customer>()
    .Property(c => c.Id)
    .Metadata.SetValueComparer(
        new ValueComparer<string>(
            (l, r) => string.Equals(l, r, StringComparison.Ordinal),
            v => v.GetHashCode()));
```

> [!WARNING]
> データベース側の列の照合順序も合わせて大文字小文字を区別するものに変更しないと、.NET 側とデータベース側で判定が食い違います。**片方だけを変えてはいけません。**

#### 文字列の主キーは EF Core 側でも大文字小文字を区別しない

ここまでの照合順序の説明はデータベース側の比較です。一方、**EF Core が変更追跡でキー値を突き合わせるときの比較**にも注意が必要です。EF Core 8.0 以降、SQL Server / Azure SQL プロバイダーでは、文字列のキー値が **.NET の大文字小文字を区別しない序数比較子**で比較されます。それ以前は大文字小文字を区別する比較子でした。

公式ドキュメントはこの変更の理由を、「SQL Server は既定で、外部キーの値が主キーの値に一致するかを大文字小文字を区別せずに比較する。EF が大文字小文字を区別して比較すると、**本来つながるはずの外部キーと主キーが結び付かないことがある**」と説明しています。

この文字列キーの確認例では、比較子は `CaseInsensitiveValueComparer` で、追跡中の `"ABC"` を小文字の `"abc"` で検索して取得できることを確認できています。

```text
キーの ValueComparer            = CaseInsensitiveValueComparer
comparer.Equals("ABC", "abc")   = True
```

区別が必要な場合は、`ValueComparer` を明示的に設定します。

```csharp
var comparer = new ValueComparer<string>(
    (l, r) => string.Equals(l, r, StringComparison.Ordinal),
    v => v.GetHashCode(),
    v => v);

modelBuilder.Entity<Blog>().Property(e => e.Id).Metadata.SetValueComparer(comparer);
```

> [!NOTE]
> ただし、比較子を大文字小文字を区別するものに変えても、**データベース側の照合順序は変わりません**。大文字小文字を厳密に区別したいなら、列の照合順序（`UseCollation`）とあわせて設計してください。

### 複数の列で並べ替えるキーセットページング

並べ替えのキーが 1 つでは一意にならない場合、キーセットページングの条件は**単純な `>` の連鎖では書けません**。次のように書くと、境界にある行が丸ごと欠落します。

```csharp
// 誤り: 同じ Date の行が残っていても次の日付へ飛んでしまう
.Where(p => p.Date > lastDate)
```

次は、`2026-01-01` と `2026-01-02` を各 3 件持つデータを、`Date` と `Id` の昇順で 2 件ずつページングする確認例です。1 ページ目は `Id=1, 2`、境界は `Date=2026-01-01, Id=2` です。

| 条件 | 生成される `WHERE` 句 | 2 ページ目の結果 |
| --- | --- | --- |
| `p.Date > lastDate` | `[p].[Date] > @lastDate` | `Id=4, 5` — **`Id=3` が欠落** |
| 公式の OR パターン | `[p].[Date] > @lastDate OR ([p].[Date] = @lastDate AND [p].[Id] > @lastId)` | `Id=3, 4` — 正しい |

公式ドキュメントが示す正しい書き方は、**最後のキー以外が等しい場合を `OR` でつなぐ**形です。

```csharp
var page = await context.Posts
    .AsNoTracking()
    .OrderBy(p => p.Date)
    .ThenBy(p => p.Id)
    .Where(p => p.Date > lastDate || (p.Date == lastDate && p.Id > lastId))
    .Take(pageSize)
    .ToListAsync(cancellationToken);
```

並べ替えキーが増えるほど、この `OR` の項も増えていきます。

> [!WARNING]
> 多くの SQL データベースは、これをより簡潔かつ効率的に書ける **行値 (row values)** 構文 `WHERE (Date, Id) > (@lastDate, @lastId)` をサポートしていますが、**EF Core は現時点でこれを LINQ で表現できません**。公式ドキュメントにもその旨が明記されており、[dotnet/efcore#26822](https://github.com/dotnet/efcore/issues/26822) で追跡されています。EF Core 10 の時点でもこの Issue は Backlog のままです。
>
> `ValueTuple.Create(p.Date, p.Id).CompareTo(...)` の形の確認例でも、SQL 翻訳の `InvalidOperationException`（`The LINQ expression ... could not be translated`）を確認できています。上記の `OR` パターンを使ってください。

> [!TIP]
> ページングでは、**並べ替えに対応するインデックスが性能を左右します**。公式ドキュメントも「ページングの並べ替えに対応するインデックスを用意すること」を求めており、複数の列で並べ替える場合はそれらをまとめた**複合インデックス (composite index)** を定義します。

> [!IMPORTANT]
> ページングでは、並び順が一意になるようにしてください。並び順が同値の行があると、ページ間で行の順序が不定になります。上の例のように、末尾に主キーを加えるのが確実です。

> [!WARNING]
> `pageNumber` と `pageSize` を外部入力から受け取る場合は、**必ず範囲を検証してください**。以下の負値の確認例では、EF Core による事前の拒否ではなく、SQL のページング句への値の受け渡しを確認できています。不正な値の扱いをデータベース側に任せないでください。
>
> SQL Server の [`OFFSET` は「0 以上」、`FETCH NEXT` は「1 以上」](https://learn.microsoft.com/ja-jp/sql/t-sql/queries/select-order-by-clause-transact-sql?view=sql-server-ver16)と定められています。次は、20 件のデータに対する確認結果です。
>
> | 入力 | LINQ 演算 | SQL Server 2022 |
> | --- | --- | --- |
> | `pageNumber=0`, `pageSize=10` | `Skip(-10).Take(10)` | `SqlException`（下記） |
> | `pageNumber=1`, `pageSize=-5` | `Skip(0).Take(-5)` | `SqlException`（下記） |
> | `pageNumber=1`, `pageSize=0` | `Skip(0).Take(0)` | 0 件 |
>
> SQL Server 側の例外メッセージはそれぞれ次のとおりです。
>
> ```text
> The offset specified in a OFFSET clause may not be negative.
> The number of rows provided for a FETCH clause must be greater then zero.
> ```
>
> この負値の確認例は SQL Server のクエリ例外です。HTTP のステータスコードは API 側の例外処理に依存し、DB の確認結果だけから 500 応答とは断定できません。外部入力は事前に検証し、次のように上限を設けてください。
>
> ```csharp
> pageNumber = Math.Max(pageNumber, 1);
> pageSize = Math.Clamp(pageSize, 1, 100);
> ```
>
> この SQL Server の `Take(0)` の確認例は 0 件で、生成 SQL はページング句ではなく `WHERE 0 = 1` を含んでいます。データベースへの問い合わせ省略の確認ではありません。
>
> ASP.NET Core で [`[ApiController]` の既定の自動 400 応答](https://learn.microsoft.com/ja-jp/aspnet/core/web-api/?view=aspnetcore-10.0#automatic-http-400-responses)を使う場合は、[第 3 章](../03-mvc-web-and-api/index.md)で扱った検証属性（`[Range(1, 100)]` など）を DTO に付けると、範囲外入力を**アクション実行前に 400 応答で拒否**できます。`[ApiController]` を使わない場合や、`SuppressModelStateInvalidFilter` を `true` にして自動応答を無効にした場合は、`ModelState` の確認とエラー応答を明示的に実装してください。

> [!TIP]
> **さらに詳しく**
>
> - [付録 EF Core 5：パフォーマンス](../appendix-efcore-05/index.md) — インデックス設計、コンパイル済みクエリ、NativeAOT

### 関連データを読み込まずに数える

本編で扱った明示的読み込み (`Collection(...).LoadAsync()`) は、関連データを**すべて**メモリに読み込みます。件数を数えたいだけの場合や、条件に合うものだけが欲しい場合は `Query()` を使います。`Query()` はそのナビゲーションに対応する `IQueryable` を返すので、後ろに LINQ を続けられます。

この節の例は、本編とは別の **投稿に評価値を持つ検証用モデル**を使います。同名の型を本編のモデルへ混ぜず、次の `RatingQuerySample` 名前空間に配置してください。以下の `context` は、SQL Server の接続文字列を `UseSqlServer` に設定した `RatingContext` です。

```csharp
using Microsoft.EntityFrameworkCore;

namespace RatingQuerySample;

public class Blog
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Post> Posts { get; set; } = [];
}

public class Post
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int Rating { get; set; }
    public int BlogId { get; set; }
    public Blog? Blog { get; set; }
}

public class RatingContext(DbContextOptions<RatingContext> options) : DbContext(options)
{
    public DbSet<Blog> Blogs => Set<Blog>();
    public DbSet<Post> Posts => Set<Post>();
}
```

```csharp
var blog = await context.Blogs.SingleAsync(b => b.Id == id, cancellationToken);

// 件数だけを数える。Post のインスタンスは 1 件も作られない
var postCount = await context.Entry(blog)
    .Collection(b => b.Posts)
    .Query()
    .CountAsync(cancellationToken);

// 条件に合うものだけを読み込む
var goodPosts = await context.Entry(blog)
    .Collection(b => b.Posts)
    .Query()
    .Where(p => p.Rating > 3)
    .ToListAsync(cancellationToken);
```

SQL Server 2022 の確認例では、前者は次の SQL で、`blog.Posts` は空のままです。

```sql
-- SQL Server
SELECT COUNT(*)
FROM [Posts] AS [p]
WHERE [p].[BlogId] = @p
```

後者は `WHERE` に条件が積まれ、返ってきた 2 件だけが追跡されます。

```sql
-- SQL Server
SELECT [p].[Id], [p].[BlogId], [p].[Rating], [p].[Title]
FROM [Posts] AS [p]
WHERE [p].[BlogId] = @p AND [p].[Rating] > 3
```

参照ナビゲーション（単一の相手）を明示的に読み込むときは `Collection` ではなく `Reference` を使います。

```csharp
await context.Entry(post)
    .Reference(p => p.Blog)
    .LoadAsync(cancellationToken);
```

> [!NOTE]
> `Collection(...)` や `Reference(...)` が返すエントリーには `IsLoaded` プロパティがあります。公式リファレンスでは「そのナビゲーションが参照するエンティティが読み込まれていると**わかっている**かどうか」と説明されており、`Include` や `Load` / `LoadAsync` がこのフラグを立てます。フラグが立っている状態で再度 `LoadAsync` を呼んでも何も起きません (no-op)。
>
> 逆に、**関連エンティティがすべて読み込まれていても `IsLoaded` が `false` のままになることがあります**。読み込まれ方によっては「全部そろっている」と判断できないためです。上の `Query().Where(...)` の確認例でも、2 件の追跡後に `IsLoaded` は `false` です。すべてを読み込みたいときは `LoadAsync` を呼びます。

### 存在チェックは Count ではなく Any を使う

「関連するレコードが 1 件でもあるか」は `Any()` で表せます。[EF Core 9 の公式の最適化例](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/whatsnew#queries-using-count--0-are-optimized)は、`b.Posts.Count > 0` を `EXISTS` に翻訳する例を示しています。次は SQL Server 2022 / EF Core 10 の同じモデルで比較した生成 SQL です。`Count` を含むすべての式への最適化を保証する表ではありません。

| LINQ の書き方 | 生成される SQL の WHERE 句 |
| --- | --- |
| `b.Posts.Any()` | `EXISTS (SELECT 1 FROM [Posts] ...)` |
| `b.Posts.Count > 0`（プロパティ） | `EXISTS (SELECT 1 FROM [Posts] ...)` |
| `b.Posts.Count != 0`（プロパティ） | `EXISTS (SELECT 1 FROM [Posts] ...)` |
| `b.Posts.Count() > 0`（**メソッド**） | `(SELECT COUNT(*) FROM [Posts] ...) > 0` |
| `b.Posts.Count() != 0`（**メソッド**） | `(SELECT COUNT(*) ...) <> 0 OR (SELECT COUNT(*) ...) IS NULL` |

この比較で確認したのは生成 SQL の違いであり、実行時間の差を測ったものではありません。生成 SQL だけを根拠に、関連レコードが増えるほど性能差も大きくなるとは判断できません。

> [!WARNING]
> **この確認例では、`Count` プロパティと `Count()` メソッドで生成 SQL が異なります。** この違いをすべてのモデルやクエリに適用される翻訳規則とは扱わないでください。存在チェックを意図する場合は `Any()` と書くと、件数そのものが必要な処理と区別できます。

同じモデルの確認例では、`Count == 0` は `COUNT(*) = 0`、`!Any()` は `NOT EXISTS` です。存在しないことは、次のように `!Any()` で表せます。

```csharp
// 良い例: NOT EXISTS になる
var emptyBlogs = await db.Blogs.Where(b => !b.Posts.Any()).ToListAsync();
```

---

### 常に Include する（AutoInclude）

エンティティをクエリ結果として読み込むときに、特定のナビゲーションも自動的に読み込むようモデル側で構成できます。

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Blog>().Navigation(b => b.Theme).AutoInclude();
}
```

次は、SQL Server 2022 でこの構成の `db.Blogs` を問い合わせる確認例です。`Include` を書かなくても JOIN を含む SQL を確認できています。

```sql
-- SQL Server
SELECT [b].[Id], [b].[Name], [b].[ThemeId], [t].[Id], [t].[Color]
FROM [Blogs] AS [b]
LEFT JOIN [Theme] AS [t] ON [b].[ThemeId] = [t].[Id]
```

公式ドキュメントは、この構成が「結果に含まれるすべてのエンティティに対して適用される」と述べています。つまり、別のエンティティの `Include` の結果としてぶら下がってきた `Blog` にも `Theme` の読み込みが付いてきます。

特定のクエリでだけ読み込みたくない場合は `IgnoreAutoIncludes()` を使います。

```csharp
var blogs = await db.Blogs.IgnoreAutoIncludes().ToListAsync();
```

```sql
-- SQL Server
SELECT [b].[Id], [b].[Name], [b].[ThemeId]
FROM [Blogs] AS [b]
```

> [!WARNING]
> **所有型へのナビゲーションは `IgnoreAutoIncludes()` では外れません。** 公式ドキュメントは、規約による所有型の自動読み込みはこの API で止められないと明記しています。所有型 `Address` を持つ `Blog` の確認例でも、`IgnoreAutoIncludes()` を付けた SELECT に `[b].[Address_City]` が含まれています。

> [!NOTE]
> [公式のナビゲーションの自動読み込み](https://learn.microsoft.com/ja-jp/ef/core/querying/related-data/eager#model-configuration-for-auto-including-navigations)は、結果にエンティティ型が返されるクエリが対象です。**画面で使わない関連データも読み込む場合がある**一方、すべてのクエリに JOIN が入るわけではありません。EF Core 10.0.11 / SQLite の確認例でも、列のみの `Select(b => new { b.Id, b.Name })` と単純な `CountAsync()` に自動読み込み用 JOIN は含まれていません。
>
> コレクションの読み込みには[分割クエリ](https://learn.microsoft.com/ja-jp/ef/core/querying/single-split-queries)も適用できます。参照 `Theme` とコレクション `Posts` を `AutoInclude` にする確認例では、`AsSplitQuery()` の SQL は 2 本で、参照側の JOIN は残っています。読み込みコストは実際のクエリの形で判断してください。

### null の比較は C# と SQL で意味が違う

SQL のデータベースは比較を **3 値論理** (`true` / `false` / `null`) で扱いますが、C# は 2 値のブール論理です。EF Core は LINQ を SQL に変換するとき、この差を埋めるために追加の null チェックを補います。

次は、SQL Server 2022 での確認例に使うエンティティです。`String1` と `String2` はどちらも null を許容します。

```csharp
var q = await context.Entities
    .Where(e => e.String1 != e.String2)
    .ToListAsync(cancellationToken);
```

生成される SQL には、C# と同じ意味になるよう補正が入ります。

```sql
-- SQL Server
SELECT [e].[Id], [e].[String1], [e].[String2]
FROM [Entities] AS [e]
WHERE ([e].[String1] <> [e].[String2] OR [e].[String1] IS NULL OR [e].[String2] IS NULL)
  AND ([e].[String1] IS NOT NULL OR [e].[String2] IS NOT NULL)
```

一方 `==` の場合はもっと単純です。**`!=` は `==` より複雑で遅くなりがち**なので、書き換えられるなら等価比較を使ってください。

```sql
-- SQL Server
SELECT [e].[Id], [e].[String1], [e].[String2]
FROM [Entities] AS [e]
WHERE [e].[String1] = [e].[String2] OR ([e].[String1] IS NULL AND [e].[String2] IS NULL)
```

null を明示的に除外しておくと、EF Core はその列を null 非許容として扱えるため SQL が単純になります。

```csharp
var q = await context.Entities
    .Where(e => e.String1 != null && e.String2 != null && e.String1 != e.String2)
    .ToListAsync(cancellationToken);
```

```sql
-- SQL Server
SELECT [e].[Id], [e].[String1], [e].[String2]
FROM [Entities] AS [e]
WHERE [e].[String1] IS NOT NULL AND [e].[String2] IS NOT NULL AND [e].[String1] <> [e].[String2]
```

`UseRelationalNulls(true)` を指定すると、この補正を無効にして SQL 本来の null の扱いをそのまま使えます。

```csharp
options.UseSqlServer(connectionString, o => o.UseRelationalNulls(true));
```

> [!WARNING]
> [公式の null 比較の説明](https://learn.microsoft.com/ja-jp/ef/core/querying/null-comparisons#using-relational-null-semantics)は、`UseRelationalNulls(true)` により **LINQ クエリの意味が C# と一致しなくなる**ことを警告しています。上の 4 行（うち `String1` か `String2` が null の行が 2 行）の確認例では、`String1 != String2` は既定で 2 件、この設定では 1 件です。後者の SQL は `WHERE [e].[String1] <> [e].[String2]` となり、null を含む行は条件を満たしません。

> [!TIP]
> null 非許容の列どうしの比較では、null 許容列の比較に必要な補償条件を省けます。公式も、可能な場合は列を null 非許容にすることを勧めています。この例では SQL の形と結果を確認しており、実行時間を比較したベンチマークではありません。

#### SQLite の bool? の null も空文字になる

SQLite プロバイダーでも、`bool?` の列に対する `ToString()` は SQL に翻訳され、値が `null` なら空文字列を返します。これは `string?` などを含むすべての型への保証ではありません。[SQL Server の例と文字列型との対照](#boolint-の-null-を文字列に変換する)に続き、ここでは SQLite での `bool?` の射影を確認します。

```csharp
var rows = await db.Items
    .Select(x => new { x.Id, Text = x.Flag.ToString() })   // Flag は bool?
    .ToListAsync();
```

SQLite のこの確認例では、次の SQL に翻訳され、`Flag` が `NULL` の行の結果は長さ 0 の文字列です。

```sql
SELECT "i"."Id", CASE "i"."Flag"
    WHEN 0 THEN 'False'
    WHEN 1 THEN 'True'
    ELSE ''
END AS "Text"
FROM "Items" AS "i"
```

これは `Nullable<T>.ToString()` が C# 側でも空文字を返す挙動に合わせたものです。以前の挙動に戻したい場合は、クエリを次のように書き換えます。

```csharp
var oldBehavior = db.Items.Select(x => x.Flag == null ? null : x.Flag.ToString());
```

### エンティティをそのまま JSON にすると循環参照で失敗する

EF Core はナビゲーションプロパティを自動的に補完 (fix-up) するため、**オブジェクトグラフに循環ができます**。`Blog` を `Include` で読み込むと `Blog.Posts` に `Post` が入り、その `Post.Blog` が元の `Blog` を指すためです。公式ドキュメントは、この循環をシリアル化フレームワークが扱えない場合があると明記しています。

次は、SQLite から `Include` で取得した `Blog` を、.NET 10 の `JsonSerializer.Serialize` に既定の設定で渡す確認例の例外です（メッセージとパスは一部省略）。

```text
System.Text.Json.JsonException: A possible object cycle was detected. This can either be
due to a cycle or if the object depth is larger than the maximum allowed depth of 64.
Path: $.Posts.Blog.Posts.Blog.Posts.Blog....
```

対処は 3 つあります。1 つ目は、`ReferenceHandler.IgnoreCycles` を指定して循環部分を `null` に置き換える方法です。最小 API では `ConfigureHttpJsonOptions`、MVC やコントローラーでは `AddControllers().AddJsonOptions(...)` で設定します。

```csharp
builder.Services.ConfigureHttpJsonOptions(
    options => options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);
```

同じデータで実測した結果は次のとおりで、循環している `blog` が `null` になります。

```json
{"id":1,"name":"A","posts":[{"id":1,"title":"P1","blogId":1,"blog":null}]}
```

2 つ目は `ReferenceHandler.Preserve` です。こちらは循環を `$id` と `$ref` の参照に置き換えます。次の JSON は、上の例と同じプロパティ・値を持つグラフを `JsonSerializer.Serialize` に渡し、`new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.Preserve }` を指定して再現したものです。最小 API の応答とは異なり、プロパティ名は `Id`・`Posts` などの元の表記を保ちます。

```json
{"$id":"1","Id":1,"Name":"A","Posts":{"$id":"2","$values":[
  {"$id":"3","Id":1,"Title":"P1","BlogId":1,"Blog":{"$ref":"1"}}]}}
```

最小 API で `ConfigureHttpJsonOptions` の指定を `ReferenceHandler.Preserve` に変える場合も参照情報を保持しますが、[Web 用の既定設定は camelCase](https://learn.microsoft.com/ja-jp/dotnet/standard/serialization/system-text-json/configure-options#web-defaults-for-jsonserializeroptions)です。ASP.NET Core 10 / SQLite の同じデータの確認例でも、キーは `id`・`posts` です。`Preserve` 自体がキー名の大小文字を切り替えるわけではありません。

3 つ目は、循環の原因になっているナビゲーションプロパティに `System.Text.Json.Serialization` 名前空間の `[JsonIgnore]` を付けて、シリアル化の対象から外す方法です。

> [!WARNING]
> [公式の参照保持の説明](https://learn.microsoft.com/ja-jp/dotnet/standard/serialization/system-text-json/preserve-references)では、`ReferenceHandler.Preserve` は複合型のシリアル化時に `$id`・`$values`・`$ref` などのメタデータを追加します。**JSON の形自体が変わる**ため、従来の配列形式を前提にしたクライアントとの互換性に注意してください。上のコレクション `Posts` の確認例でも、`$id` と `$values` を持つオブジェクトが出力されています。参照形式を採用する場合は、クライアント側もその形式に対応させます。

> [!TIP]
> API のレスポンスにエンティティを直接使わず、親への戻り参照を含めない DTO に投影すれば、この循環を避けられます。詳しくは[第8章の投影による最適化](../08-entity-framework-core/index.md#投影-projection-による最適化)を参照してください。

## 3. SQL を直接扱う

### 生の SQL を使う

LINQ で表現できないクエリや、ストアドプロシージャの呼び出しには生の SQL を使います。生の SQL と `SaveChanges` を 1 つのトランザクションにまとめる場合は[付録4の「セーブポイント」](../appendix-efcore-04/index.md#セーブポイント)と[「接続の回復性とトランザクションの併用」](../appendix-efcore-04/index.md#接続の回復性とトランザクションの併用)もあわせて確認してください。

```csharp
var blogs = await context.Blogs
    .FromSql($"SELECT * FROM [Blogs] WHERE [Url] LIKE {pattern}")
    .AsNoTracking()
    .ToListAsync(cancellationToken);
```

`FromSql` は **補間文字列 (FormattableString)** の埋込値を SQL パラメーターに変換し、SQL として連結しません。次は、上の `LIKE` クエリを SQLite で実行し、`pattern` に `https://ok.example.com' OR '1'='1` を渡す確認例です。`Url` が `https://ok.example.com` の行に対してこの値は一致せず、結果は 0 件です。

```text
.param set p0 'https://ok.example.com'' OR ''1''=''1'

SELECT * FROM [Blogs] WHERE [Url] LIKE @p0
```

> [!WARNING]
> [公式の SQL クエリの制約](https://learn.microsoft.com/ja-jp/ef/core/querying/sql-queries#limitations)では、`FromSql` でエンティティ型を返すには、次の 2 つを満たす必要があります。
>
> - SQL が、そのエンティティ型の**すべてのプロパティ分のデータを返す**こと
> - 結果セットの**列名が、プロパティのマップ先の列名と一致する**こと
>
> 次は、`Id` / `Name` / `Owner` を同名の列にマップした `Blog` で、`Owner` の省略と `Name` の別名指定を比較する確認例です。どちらも必要な列が見つからない例外を確認できています。
>
> ```csharp
> // どちらも InvalidOperationException
> await context.Blogs.FromSql($"SELECT [Id], [Name] FROM [Blogs]").ToListAsync();
> await context.Blogs.FromSql($"SELECT [Id], [Name] AS BlogName, [Owner] FROM [Blogs]").ToListAsync();
> ```
>
> ```text
> InvalidOperationException: The required column 'Owner' was not present in the results of a 'FromSql' operation.
> InvalidOperationException: The required column 'Name' was not present in the results of a 'FromSql' operation.
> ```
>
> `SELECT *` を使っていれば普通は問題になりませんが、列を絞ったり別名を付けたりすると起きます。列を絞りたい場合は、次に説明する未マップ型を使ってください。

> [!WARNING]
> **パラメーター化と、`LIKE` のワイルドカードの扱いは別です。** T-SQL の `%` は「0 文字以上の任意の文字列」に一致するため、入力が `%` なら `LIKE '%'` として扱われます。上のデータの確認例でも、`FromSql` と `EF.Functions.Like` の両方で全行への一致を確認できています。
>
> ```csharp
> // pattern = "%" のとき、どちらも全行が返る
> await context.Blogs.FromSql($"SELECT * FROM [Blogs] WHERE [Url] LIKE {pattern}").ToListAsync();
> await context.Blogs.Where(b => EF.Functions.Like(b.Url, pattern)).ToListAsync();
> ```
>
> 一方、`string.Contains` を使った場合は EF Core がメタ文字をエスケープするため、`Contains("%")` は「文字としての `%` を含む行」を探し、0 件になります。
>
> ```csharp
> // こちらは "%" を文字として扱う
> await context.Blogs.Where(b => b.Url.Contains(pattern)).ToListAsync();
> ```
>
> 部分一致検索を外部入力で行う場合は、`Contains` / `StartsWith` / `EndsWith` を使うか、`LIKE` を使うなら `%` と `_` を自分でエスケープしてください。大量データに対する `LIKE '%...'` は全表スキャンを誘発するため、性能面でも入力の検証が必要です。

エンティティ型を返さないスカラークエリには `SqlQuery` を使います。

```csharp
var ids = await context.Database
    .SqlQuery<int>($"SELECT [Id] FROM [Blogs] WHERE [CreatedAt] > {threshold}")
    .ToListAsync(cancellationToken);
```

> [!IMPORTANT]
> `SqlQuery` の結果に LINQ 演算子を続けて適用する場合、EF Core は指定した SQL をサブクエリとして包み、その出力列を `Value` という名前で参照します。そのため、**出力列に `AS [Value]` という別名を付ける必要があります**。別名を付けずに `FirstAsync` や `Where` を続けると、「`Value` という列がない」という実行時エラーになります。
>
> ```csharp
> // LINQ を合成する場合は AS [Value] が必要
> var recentIds = await context.Database
>     .SqlQuery<int>($"SELECT [Id] AS [Value] FROM [Blogs] WHERE [CreatedAt] > {threshold}")
>     .Where(id => id > 100)
>     .ToListAsync(cancellationToken);
> ```

#### 未マップ型 (DTO) を直接受け取る

`SqlQuery` が扱えるのはスカラー値だけではありません。**EF Core のモデルに含まれていない、マッピング可能な CLR 型**にも結果を詰められます（EF Core 8.0 で追加）。複数のテーブルを結合した結果や、列の部分集合をそのまま DTO に受け取れるため、生の SQL を書くときに `DbCommand` などの低レベルな API へ降りる必要がなくなります。

この節の `Blog` / `Post` と `context` も、[「関連データを読み込まずに数える」の検証用モデル](#関連データを読み込まずに数える)（`RatingQuerySample.RatingContext`）を使います。`Rating` はこのモデルの `Post` に定義されています。本編の `BloggingContext` にそのまま貼り付ける例ではありません。

```csharp
public class PostSummary
{
    public string BlogName { get; set; } = "";
    public string PostTitle { get; set; } = "";
    public int Rating { get; set; }
}
```

```csharp
var summaries = await context.Database
    .SqlQuery<PostSummary>(
        $"""
        SELECT b.[Name] AS BlogName, p.[Title] AS PostTitle, p.[Rating]
        FROM [Posts] AS p INNER JOIN [Blogs] AS b ON p.[BlogId] = b.[Id]
        """)
    .ToListAsync(cancellationToken);
```

型はデータベースのどのテーブルとも一致している必要がありません。パラメーター付きコンストラクターや `[Column]` 属性など、EF Core のマッピング機構も使えます。結果は変更追跡の対象ではなく、この確認例でも `ChangeTracker.Entries()` は 0 件です。

> [!NOTE]
> スカラーの `SqlQuery` と違い、すべての値を `Value` と命名する必要はありません。この例では **各プロパティに対応する同名の列**を返します。次は `Where` を合成する確認例の SQL で、元の SQL がサブクエリとして使われています。
>
> ```sql
> SELECT [p].[BlogName], [p].[PostTitle], [p].[Rating]
> FROM (
>     SELECT b.[Name] AS BlogName, p.[Title] AS PostTitle, p.[Rating]
>     FROM [Posts] AS p INNER JOIN [Blogs] AS b ON p.[BlogId] = b.[Id]
> ) AS [p]
> WHERE [p].[Rating] >= 5
> ```

> [!WARNING]
> 未マップ型には **キーが定義されず、他の型へのリレーションシップも持てません。** ナビゲーションに見えるプロパティ（他のエンティティ型やそのコレクション）を含めると、実行時に次の例外になります。
>
> ```text
> InvalidOperationException: The property 'BadDto.Posts' of type 'List<Post>' appears to be
> a navigation to another entity type. Navigations are not supported when using 'SqlQuery".
> Either include this type in the model and use 'FromSql' for the query, or ignore this
> property using the '[NotMapped]' attribute.
> ```
>
> リレーションシップが必要な型はモデルにマップして `FromSql` を使うか、そのプロパティに `[NotMapped]` を付けてください。
>
> [未マップ型の公式の説明](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-8.0/whatsnew#raw-sql-queries-for-unmapped-types)も、型にクエリ結果の各値に対応するプロパティが必要としています。型のマッピングと SQL の返却列を合わせてください。上の SQL から `PostTitle` を省いた確認例では、必要な列が存在しないことを示す例外が確認できています。

ここまでの例は、生の SQL を書かずに LINQ の `Select` だけでも同じ結果が得られます。SQL を書く必要が本当にあるのかは、先に検討してください。

```csharp
// 上の Where を合成した SQL と同じ結果を LINQ だけで得る
var summaries = await context.Posts
    .Select(p => new PostSummary
    {
        BlogName = p.Blog!.Name,
        PostTitle = p.Title,
        Rating = p.Rating,
    })
    .Where(x => x.Rating >= 5)
    .ToListAsync(cancellationToken);
```

更新系の SQL は `ExecuteSqlAsync` です。

```csharp
await context.Database.ExecuteSqlAsync(
    $"UPDATE [Blogs] SET [Name] = {newName} WHERE [Id] = {id}",
    cancellationToken);
```

> [!WARNING]
> `FromSqlRaw` / `ExecuteSqlRaw` は文字列をそのまま SQL として扱うため、ユーザー入力を連結すると **SQL インジェクション** の脆弱性になります。可変値は必ずパラメーターとして渡し、どうしても `Raw` 系を使う場合は `new SqlParameter(...)` を明示的に指定してください。
>
> EF Core には、この誤りをコンパイル時に検出するアナライザーが 2 つあります。どちらもカテゴリーは **Security**、重大度は **Warning** です。
>
> | 診断 ID | 検出する書き方 | 導入バージョン |
> | --- | --- | --- |
> | `EF1002` | `FromSqlRaw` に **補間文字列** を渡す | EF Core 8 |
> | `EF1003` | `FromSqlRaw` に **連結した文字列** を渡す | EF Core 10 |
>
> 実際に両方の書き方を含むコードをビルドすると、次の 2 件が報告されます（実測で確認済み）。
>
> ```text
> warning EF1002: Method 'FromSqlRaw' inserts interpolated strings directly into the SQL,
> without any protection against SQL injection. Consider using 'FromSql' instead...
>
> warning EF1003: Method 'FromSqlRaw' inserts concatenated strings directly into the SQL,
> without any protection against SQL injection. Consider using 'FromSql' instead...
> ```
>
> **特に `EF1002` は見落としやすい落とし穴です。** `FromSql` に補間文字列を渡すと自動的にパラメーター化されるのに対し、`FromSqlRaw` に同じ見た目の補間文字列を渡すと、値が SQL に直接埋め込まれてしまいます。メソッド名を `FromSql` から `FromSqlRaw` に変えるだけで、安全なコードが危険なコードに変わるということです。
>
> CI では `<WarningsAsErrors>EF1002;EF1003</WarningsAsErrors>` を設定して、これらの警告をビルドエラーに昇格させておくと確実です。

```csharp
// 危険：絶対に書かない（EF1003 警告が出る）
var unsafeBlogs = await context.Blogs
    .FromSqlRaw("SELECT * FROM [Blogs] WHERE [Url] = '" + userInput + "'")
    .ToListAsync(cancellationToken);

// 危険：見た目は FromSql とほぼ同じだが値が埋め込まれる（EF1002 警告が出る）
var alsoUnsafeBlogs = await context.Blogs
    .FromSqlRaw($"SELECT * FROM [Blogs] WHERE [Url] = '{userInput}'")
    .ToListAsync(cancellationToken);

// 安全：パラメーター化する
var safeBlogs = await context.Blogs
    .FromSqlRaw("SELECT * FROM [Blogs] WHERE [Url] = {0}", userInput)
    .ToListAsync(cancellationToken);
```

#### 列名は動的にできない

パラメーターにできるのは**値だけ**です。公式ドキュメントは「データベースは列名（やスキーマのその他の部分）のパラメーター化を許可しない」と明記しています。次のように列名を補間しても意図どおりには動きません。

```csharp
var columnName = "Owner";
var columnValue = "johndoe";

// 動かない
var blogs = await context.Blogs
    .FromSql($"SELECT * FROM [Blogs] WHERE {columnName} = {columnValue}")
    .ToListAsync(cancellationToken);
```

> [!WARNING]
> 次は、このコードが行う比較を示す説明用の SQL です。実際の EF Core の実行では値を `DbParameter` として渡しますが、ここでは値を分かりやすく示すため、[SQL Server のローカル変数宣言](https://learn.microsoft.com/ja-jp/sql/t-sql/language-elements/declare-local-variable-transact-sql?view=sql-server-ver16)を付けています。
>
> ```sql
> DECLARE @p0 nvarchar(4000) = N'Owner';
> DECLARE @p1 nvarchar(4000) = N'johndoe';
>
> SELECT * FROM [Blogs] WHERE @p0 = @p1
> ```
>
> [`ToQueryString()` の公式 API 説明](https://learn.microsoft.com/ja-jp/dotnet/api/microsoft.entityframeworkcore.entityframeworkqueryableextensions.toquerystring?view=efcore-10.0)は、出力はデバッグ用であり、直接実行に適さない場合があるとしています。診断表示と実際に送信するコマンド・パラメーターは区別してください。
>
> この例では列名まで文字列パラメーターになり、条件は `'Owner' = 'johndoe'` という偽の文字列比較です。上の C# コードを SQL Server 2022 で実行する確認例では、**例外ではなく、該当データがあるのに結果は 0 件**となることを確認できています。期待する結果が 0 件のテストだけでは、この誤りを見落とします。

どうしても列名を動的に組み立てる必要がある場合は、公式ドキュメントが示すとおり `FromSqlRaw` を使い、**列名は文字列補間で埋め込み、値は `DbParameter` として渡します。**

```csharp
var columnName = "Owner";
var columnValue = new SqlParameter("columnValue", "johndoe");

var blogs = await context.Blogs
    .FromSqlRaw($"SELECT * FROM [Blogs] WHERE {columnName} = @columnValue", columnValue)
    .ToListAsync(cancellationToken);
```

同じデータを使うこの形の確認例では、期待する 1 件の取得を確認できています。ただし `FromSqlRaw` への補間文字列により **`EF1002` の警告が出ます**。列名が安全な出所であることを確認したうえで、その箇所だけ警告を抑制してください。

> [!IMPORTANT]
> 公式ドキュメントは、実装方法の前に**そもそも動的に組み立てるべきかを考えるよう**促しています。列名をユーザーから受け取ると、**インデックスのない列を選ばれてクエリが極端に遅くなりデータベースに過負荷をかける**おそれや、**公開したくないデータを含む列を選ばれる**おそれがあります。本当に動的でなければならない場面を除き、**2 つの列名に対しては 2 つのクエリを書くほうがよい**というのが公式の助言です。

`FromSql` の結果には LINQ を続けて適用できるため、共通部分だけ SQL で書き、絞り込みや並べ替えは LINQ に任せるといった使い分けが可能です。

```csharp
var blogs = await context.Blogs
    .FromSql($"SELECT * FROM [Blogs] WHERE [Rating] > {minRating}")
    .Where(b => b.Url.Contains("dotnet"))
    .OrderBy(b => b.Name)
    .ToListAsync(cancellationToken);
```

#### 合成できる SQL には条件がある

ただし LINQ を続けられるのは、**渡した SQL が合成可能 (composable) な場合だけ**です。EF Core は与えられた SQL を**サブクエリとして扱う**ため、サブクエリに置けない構文が含まれていると失敗します。公式ドキュメントは合成できない例として次を挙げています。

| 書き方 | SQL Server 2022 での実測結果 |
| --- | --- |
| `SELECT * FROM [Blogs] WHERE ...` | 成功 |
| 末尾にセミコロンを付ける | `SqlException: Incorrect syntax near ';'.` |
| `TOP` や `OFFSET` を伴わない `ORDER BY` を含める | `SqlException: The ORDER BY clause is invalid in views, inline functions, derived tables, subqueries, and common table expressions, unless TOP, OFFSET or FOR XML is also specified.` |

このほか、SQL Server ではクエリレベルのヒント（`OPTION (HASH JOIN)` など）を末尾に付けた SQL も合成できません。**合成する予定の SQL は `SELECT` で始まり、末尾のセミコロンを付けないのが基本**だと覚えておいてください。

#### ストアドプロシージャを実行する

`FromSql` はストアドプロシージャの実行にも使えます。

```csharp
var blogs = await context.Blogs
    .FromSql($"EXECUTE dbo.GetPopularBlogs {minRating}")
    .ToListAsync(cancellationToken);
```

補間した値は通常の `FromSql` と同じく `DbParameter` となり、SQL として連結されません。省略可能なパラメーターがあるストアドプロシージャでは、次のように `SqlParameter` で **名前付きパラメーター**を指定できます。

```csharp
var p = new SqlParameter("p", 5);

var blogs = await context.Blogs
    .FromSql($"EXECUTE dbo.GetPopularBlogs @minRating={p}")
    .ToListAsync(cancellationToken);
```

> [!WARNING]
> **SQL Server はストアドプロシージャの呼び出しに対する合成を許可しません。** 次は、`FromSql` に `Where` を合成して `ToListAsync` を呼ぶ確認例の例外です。`Where` による組立時ではなく、実行を試みる段階で確認できています。
>
> ```text
> InvalidOperationException: 'FromSql' or 'SqlQuery' was called with non-composable SQL
> and with a query composing over it. Consider calling 'AsEnumerable' after the method
> to perform the composition on the client side.
> ```
>
> 公式ドキュメントの指示どおり、`FromSql` の**直後に `AsEnumerable()` または `AsAsyncEnumerable()` を挟む**と、EF Core が合成を試みなくなり正しく動作します。ただしメッセージにあるとおり、その後の絞り込みは**クライアント側での処理**になります。データベースから返る行数が多いと無駄が大きいので、絞り込み条件はストアドプロシージャの引数として渡すほうが適切です。
>
> ```csharp
> var blogs = context.Blogs
>     .FromSql($"EXECUTE dbo.GetPopularBlogs {minRating}")
>     .AsEnumerable()
>     .Where(b => b.Name.Contains("dotnet"))
>     .ToList();
> ```

> [!NOTE]
> 公式ドキュメントは「渡すパラメーターはストアドプロシージャの定義と**厳密に一致していなければならない**」と注意しています。順序を間違えたり抜かしたりしないよう気をつけるか、上記の名前付きパラメーター記法を使ってください。型と、サイズ・精度・スケールなどの属性も対応させる必要があります。

結果セットを返さないストアドプロシージャは `ExecuteSql` で呼び出します。この 3 行を更新する確認例では、戻り値の影響行数は `3` です。

```csharp
var affected = await context.Database
    .ExecuteSqlAsync($"EXECUTE dbo.BumpRatings {delta}", cancellationToken);
```

> [!TIP]
> `FromSql` の結果は通常の LINQ クエリと **同じ変更追跡の規則**に従います。通常のエンティティ型は既定で追跡されるため、読み取り専用なら `AsNoTracking()` を付けてください。このストアドプロシージャから 2 件取得する確認例でも、直後の `ChangeTracker.Entries()` は 2 件です。

### ユーザー定義関数とビューをマッピングする

EF Core の公式パフォーマンスガイダンスは、EF が生成しない最適な SQL を使いたい場合の手段を **3 つ**挙げています。1 つ目が前節の `FromSql` で、残りの 2 つが**ユーザー定義関数 (User-Defined Function: UDF)** と**データベースビュー**です。`FromSql` は「その 1 か所でしか使わない SQL」に向く一方、複数のクエリから再利用したいロジックは関数やビューにするほうが管理しやすくなります。ビューや集計結果を主キーのない型として読む方法は[付録2の「キーなしエンティティ型でビューや集計結果を読む」](../appendix-efcore-02/index.md#キーなしエンティティ型でビューや集計結果を読む)で扱います。

#### スカラー関数

戻り値が単一の値である**スカラー関数**は、シグネチャを合わせた CLR メソッドを定義し、`HasDbFunction` でマッピングします。**引数を含めて SQL に翻訳できる場合**は CLR メソッドの本体を実行せず、データベース関数を呼び出します。次の本体は、CLR 側で誤って実行された場合に例外を投げる実装です。

```csharp
public class BloggingContext : DbContext
{
    public int PostCountForBlog(int blogId) => throw new NotSupportedException();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDbFunction(
            typeof(BloggingContext).GetMethod(nameof(PostCountForBlog), [typeof(int)])!);
    }
}
```

```csharp
var names = await context.Blogs
    .Where(b => context.PostCountForBlog(b.Id) > 1)
    .Select(b => b.Name)
    .ToListAsync(cancellationToken);
```

次は SQL Server 2022 での生成 SQL の確認例です。`WHERE` 句に関数呼出しを確認できています。

```sql
SELECT [b].[Name]
FROM [Blogs] AS [b]
WHERE [dbo].[PostCountForBlog]([b].[Id]) > 1
```

> [!WARNING]
> 「マッピングしたメソッドの本体は絶対に呼ばれない」とは限りません。公式の[UDF マッピング](https://learn.microsoft.com/ja-jp/ef/core/querying/user-defined-function-mapping)は、引数を翻訳できない場合を例外として挙げています。[クライアント評価](https://learn.microsoft.com/ja-jp/ef/core/querying/client-eval)が許される最終 `Select` では、CLR 本体が実行される場合があります。一方、`Where` 内の翻訳不能な式は実行時例外になります。上の `NotSupportedException` を投げる本体は、クライアント側でも同じ結果を計算する代替実装ではありません。
>
> EF Core 10.0.11 / SQLite の文字数関数にマップしたメソッドで 1 行取得する確認例では、翻訳可能な引数の CLR 本体の呼出回数は 0 回、最終 `Select` の翻訳不能な引数では 1 回です。同じ式を `Where` に置く確認例では、SQL 発行前の `InvalidOperationException` を確認できています。

> [!NOTE]
> NULL 許容の UDF では、引数に対する `PropagatesNullability()` の設定により、関数を再評価せず引数の `IS NULL` で判定できる場合があります。EF Core 10.0.11 / SQL Server 2022 の文字数関数に NULL と非 NULL を渡す確認例でも、関数呼出しの `IS NULL` が入力列の `IS NULL` に置き換わり、結果が一致することを確認できています。
>
> **設定した引数が NULL であることだけが、関数が NULL を返す原因である場合に限って使ってください。** これは公式 UDF ガイドの注意事項です。「NULL を返すことがある関数」すべてに付ける設定ではありません。

#### テーブル値関数 (TVF)

行の集合を返す**テーブル値関数 (Table-Valued Function: TVF)** は、`IQueryable<T>` を返す CLR メソッドとしてマッピングします。本体には `FromExpression` を書きます。

```csharp
public IQueryable<Post> PopularPosts(int likeThreshold)
    => FromExpression(() => PopularPosts(likeThreshold));

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.HasDbFunction(
        typeof(BloggingContext).GetMethod(nameof(PopularPosts), [typeof(int)])!);
}
```

`DbSet` と同じように **LINQ を合成できる**のが利点です。SQL Server の次の確認例でも、TVF に `Where` を続けたクエリは 1 本の SQL です。

```csharp
var titles = await context.PopularPosts(5)
    .Where(p => p.Title.StartsWith("a"))
    .Select(p => p.Title)
    .ToListAsync(cancellationToken);
```

```sql
SELECT [p].[Title]
FROM [dbo].[PopularPosts](@likeThreshold) AS [p]
WHERE [p].[Title] LIKE N'a%'
```

> [!NOTE]
> 公式ドキュメントは「クエリ可能な関数はテーブル値関数にマッピングしなければならない」と明記しています。また `HasTranslation` はスカラー関数専用で、テーブル値関数には使えません。

#### ビューとキーレスエンティティ型

パラメーターが不要なら、ビューをマッピングする方法もあります。公式ドキュメントは「関数と違い、**ビューはパラメーターを受け取れない**」とその違いを説明しています。ビューには主キーがないことが多いため、**キーレスエンティティ型 (keyless entity type)** として定義します。

```csharp
[Keyless]
public class BlogPostCount
{
    public string BlogName { get; set; } = "";
    public int PostCount { get; set; }
}

modelBuilder.Entity<BlogPostCount>()
    .HasNoKey()
    .ToView("View_BlogPostCounts");
```

`BlogPostCount` はモデルに登録済みなので、専用の `DbSet` プロパティを追加せず、`Set<BlogPostCount>()` から問い合わせられます。

```csharp
var counts = await context.Set<BlogPostCount>()
    .OrderBy(v => v.BlogName)
    .ToListAsync(cancellationToken);
```

キーレスエンティティ型は公式ドキュメントで「**DbContext で変更追跡されることが決してなく、したがって挿入・更新・削除もされない**」と定義されています。上の確認例でも、直後の `ChangeTracker.Entries()` は 0 件です。`AsNoTracking()` の指定は不要です。

> [!WARNING]
> [公式のビューマッピングの説明](https://learn.microsoft.com/ja-jp/ef/core/modeling/entity-types#view-mapping)では、**EF Core はビューが既に存在するものとして扱い、マイグレーションで自動作成しません。** 上のモデルの `GenerateCreateScript()` にも、ビューの DDL が含まれないことを確認できています。マイグレーションで管理する場合は、`migrationBuilder.Sql(...)` に `CREATE VIEW` を記述してください。

> [!TIP]
> EF Core から見た `ToView` の対象は「読み取り専用のクエリソース」であり、公式ドキュメントによれば**実際にデータベースビューである必要はありません**。読み取り専用として扱いたい通常のテーブルを指定することもできます。

---

### SQL Server 固有の関数を LINQ から呼ぶ

`EF.Functions` には、プロバイダーごとの固有関数を LINQ から呼ぶための拡張メソッドが用意されています。SQL Server では日付差分や型判定などが翻訳されます。

```csharp
var q = db.Events.Where(e => EF.Functions.DateDiffDay(e.Start, e.End) > 10);
```

```sql
SELECT [e].[Id], [e].[End], [e].[Start], [e].[Title]
FROM [Evs] AS [e]
WHERE DATEDIFF(day, [e].[Start], [e].[End]) > 10
```

`DateDiffDay` のほかに `DateDiffMonth` や `DateDiffYear` などがあり、`DATEDIFF` の第 1 引数に対応します。2026-01-01 から 2026-03-15 までの `DateDiffMonth` の確認例は `2` です。**経過日数ではなく、指定単位の境界をまたいだ回数**に対応します。

`IsDate` は SQL Server の `ISDATE` に翻訳されます。

```sql
WHERE CAST(ISDATE([e].[Title]) AS bit) = CAST(1 AS bit)
```

> [!WARNING]
> [公式の SQL Server 全文検索ガイド](https://learn.microsoft.com/ja-jp/ef/core/providers/sql-server/full-text-search)では、`EF.Functions.Contains` と `EF.Functions.FreeText` は **全文検索の述語**に翻訳され、事前に全文検索カタログと全文検索インデックスが必要です。次は、対象テーブルに全文検索インデックスを作らない確認例のエラーです。
>
> ```text
> Cannot use a CONTAINS or FREETEXT predicate on table or indexed view 'Evs' because it is not full-text indexed.
> ```
>
> 文字列の部分一致が目的なら、全文検索とは区別し、`EF.Functions.Like` または `string.Contains` を使ってください。

全文検索カタログと全文検索インデックスは、EF Core 10 ではモデルから構成できません。公式ドキュメントは、空のマイグレーションを追加して SQL を直接書く方法を案内しています。

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql(
        sql: "CREATE FULLTEXT CATALOG ftCatalog AS DEFAULT;",
        suppressTransaction: true);

    migrationBuilder.Sql(
        sql: "CREATE FULLTEXT INDEX ON Articles(Contents) KEY INDEX PK_Articles;",
        suppressTransaction: true);
}
```

`KEY INDEX` には、その表の一意・非 NULL・単一列のインデックス名（通常は主キーのインデックス）を指定します。マイグレーションの一部の操作はトランザクション内で実行できないため、`suppressTransaction: true` でトランザクションから外します（[付録 EF Core 4 の「保存をストアドプロシージャに割り当てる」](../appendix-efcore-04/index.md#保存をストアドプロシージャに割り当てる)でもこの指定の用途を説明しています）。

次は、全文検索を有効にした SQL Server 2022 でカタログとインデックスを作成した後の確認例です。`EF.Functions.Contains` の以下の SQL への翻訳と、該当行の取得を確認できています。

```sql
WHERE CONTAINS([a].[Contents], N'vegetables')
```

## 4. 参考ドキュメント

- [マイグレーションの概要 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/managing-schemas/migrations/)
- [マイグレーションの適用 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/managing-schemas/migrations/applying)
- [単一クエリと分割クエリ | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/querying/single-split-queries)
- [関連データの一括読み込み | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/querying/related-data/eager)
- [関連データの明示的読み込み | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/querying/related-data/explicit)
- [クエリでの null 値の比較 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/querying/null-comparisons)
- [NavigationEntry.IsLoaded プロパティ | Microsoft Learn](https://learn.microsoft.com/ja-jp/dotnet/api/microsoft.entityframeworkcore.changetracking.navigationentry.isloaded?view=efcore-10.0)
- [関連データとシリアル化 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/querying/related-data/serialization)
- [照合順序と大文字小文字の区別 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/miscellaneous/collations-and-case-sensitivity)
- [SQL クエリ | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/querying/sql-queries)
- [マイグレーションの管理 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/managing-schemas/migrations/managing)
- [EF Core 9 の破壊的変更 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/breaking-changes)
- [EF Core 8 の破壊的変更 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-8.0/breaking-changes)
- [ユーザー定義関数のマッピング | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/querying/user-defined-function-mapping)
- [クライアント評価とサーバー評価 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/querying/client-eval)
- [SQL Server プロバイダーの関数マッピング | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/sql-server/functions)
- [SQL Server プロバイダーの全文検索 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/sql-server/full-text-search)
- [SQLite プロバイダーの関数マッピング | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/sqlite/functions)
- [SQL Server プロバイダーのその他の考慮事項 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/sql-server/misc)
- [Azure Cosmos DB プロバイダーの制限事項 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/cosmos/limitations)
- [.NET Core CLI での EF Core ツールのリファレンス | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/cli/dotnet)
- [データのシード処理 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/modeling/data-seeding)
- [参照を保持して循環参照を扱う | Microsoft Learn](https://learn.microsoft.com/ja-jp/dotnet/standard/serialization/system-text-json/preserve-references)
- [エンティティ型のビューマッピング | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/modeling/entity-types#view-mapping)
- [EF Core 8 の未マップ型の SQL クエリ | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-8.0/whatsnew#raw-sql-queries-for-unmapped-types)
- [EF Core 9 の Count の最適化 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/whatsnew#queries-using-count--0-are-optimized)
- [EF Core 10.0.11 のモデル変更検出 | GitHub](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore.Design/Design/Internal/MigrationsOperations.cs)
- [EF Core 10.0.11 の CLI 終了コード | GitHub](https://github.com/dotnet/efcore/blob/v10.0.11/src/ef/Program.cs)
- [EF Core 10 の LeftJoin / RightJoin 対応 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-10.0/whatsnew#support-for-the-net-10-leftjoin-and-rightjoin-operators)

# ASP.NET Core ハンドブック

## 目次

| 章 | タイトル | 内容 |
|---|---|---|
| 第 1 章 | [開発環境セットアップ](docs/01-setup-dev-env.md) | <ol><li>.NET SDK / ランタイムとは</li><li>LTS/STS のサポート期間</li><li>OS 別セットアップ（Windows / macOS / Linux）</li><li>IDE（Visual Studio / VS Code）準備</li><li>.NET CLI 基本コマンド</li><li>初回プロジェクト作成</li><li>まとめ</li><li>参考ドキュメント</li></ol> |
| 第 2 章 | [ソリューションとプロジェクト](docs/02-solutions-and-projects.md) | <ol><li>ソリューションとプロジェクトの違いと役割</li><li>プロジェクト参照と NuGet パッケージ参照の使い分け</li><li>複数プロジェクト構造のパターン</li><li>ビルド／実行フロー</li><li>デバッグ設定・起動構成の管理</li><li>参考ドキュメント</li></ol> |
| 第 3 章 | [MVC Web アプリケーション / API](docs/03-mvc-web-and-api.md) | <ol><li>MVC (Model-View-Controller) 構成の基本</li><li>コントローラー／アクションの書き方</li><li>ルーティング（属性ルーティング / 規約ベースルーティング）</li><li>モデルバインディング / 入力検証 (Validation)</li><li>API を MVC コントローラーで実装する方法（ControllerBase, [ApiController] 属性）</li><li>フィルター（ActionFilter, ExceptionFilter 等）と横断処理</li><li>参考ドキュメント</li></ol> |
| 第 4 章 | [Minimal API](docs/04-minimal-api.md) | <ol><li>Minimal API とは何か？（MVC ベースとどう違うか）</li><li>Minimal API の基本構文（MapGet, MapPost 等）</li><li>DI を使ったハンドラ注入、MapGroup・エンドポイントグループ化などの構成パターン</li><li>Minimal API の制約・注意点と MVC との比較（構造、可読性、機能面）</li><li>どのようなケースで使うかの判断基準</li><li>参考ドキュメント</li></ol> |
| 第 5 章 | [アプリ設定 (Configuration)](docs/05-configuration.md) | <ol><li>appsettings.json / 環境別設定（Development / Production 等）</li><li>環境変数 / コマンドライン引数 / シークレット管理</li><li>IOptions&lt;T&gt; / オプションバインディング</li><li>設定のオーバーライド順序と仕組み</li><li>参考ドキュメント</li></ol> |
| 第 6 章 | [依存性注入 (DI)](docs/06-dependency-injection.md) | <ol><li>DI の基本概念（Interface と具象クラス）</li><li>サービスの登録スコープ（Transient / Scoped / Singleton）</li><li>ファクトリ登録、条件付き登録、手動スコープ</li><li>サービス層設計パターン</li></ol> |
| 第 7 章 | ファイルアップロードと外部ストレージへの保存（予定） | <ol><li>ASP.NET Core におけるファイル受信（Buffered モデル / ストリーミング型アップロード）<ul><li>小さいファイル・大きいファイルの違い、ストリーミング方式のメリットなど</li></ul></li><li>Azure Blob Storage 等クラウドストレージへの保存方法<ul><li>BlobClient / BlobContainerClient 利用、ストリームアップロード、上書き制御など</li></ul></li><li>DI によるストレージクライアント注入設計<ul><li>保存先パス設計、アクセス制御（公開／非公開）、メタデータ管理</li></ul></li></ol> |
| 第 8 章 | データベースアクセスと ORM（Entity Framework Core）（予定） | <ol><li>概要と設計方針</li><li>モデル定義と DbContext 設計</li><li>マイグレーションとスキーマ管理</li><li>クエリ操作と LINQ</li><li>更新／変更操作とトランザクション</li><li>パフォーマンス最適化</li><li>読み取り専用レプリカ (Read-Only Replica) の扱い</li><li>テスト戦略とアーキテクチャ例</li></ol> |
| 第 9 章 | 認証と認可（予定） | <ol><li>ASP.NET Core Identity 入門</li><li>外部ログイン（OAuth / OpenID Connect）</li><li>JWT 認証 (Bearer トークン) の導入</li><li>ポリシーベース認可 / ロールベース認可</li><li>認証・認可ミドルウェア設定</li></ol> |
| 第 10 章 | テスト入門（予定） | <ol><li>単体テスト (xUnit / NUnit)</li><li>モックライブラリ活用 (Moq 等)</li><li>DI を活かしたテスト設計</li><li>Web アプリケーション統合テスト (WebApplicationFactory など)</li><li>外部 API 呼び出し部分のモック化</li></ol> |
| 第 11 章 | デプロイと運用（予定） | <ol><li>オンプレミス/Azure への展開（他社クラウドは言及のみ）</li><li>Docker コンテナ化 / Kubernetes 組み込み</li><li>環境変数・シークレット・Key Vault (Azure) の利用</li><li>ロギング・メトリクス収集 / モニタリング</li><li>障害対応・自動再起動 / フォールトトレランス</li><li>バージョンアップ・マイグレーション戦略</li></ol> |
| 第 12 章 | セキュリティ（予定） | <ol><li>通信レイヤーの保護 (HTTPS, HSTS, TLS 設定)</li><li>CSRF（クロスサイトリクエストフォージェリ）対策<ul><li>Anti-Forgery Token の仕組み・属性指定・AJAX 対応</li></ul></li><li>CORS（Cross-Origin Resource Sharing）制御<ul><li>ポリシー定義、AllowAnyOrigin / AllowCredentials の注意、プリフライト対応</li></ul></li><li>入力検証 / モデルバインディング検証（SQL インジェクション, バリデーション制御）</li><li>XSS（クロスサイトスクリプティング）対策（エスケープ／Content Security Policy 等）</li><li>Clickjacking 対策（X-Frame-Options, CSP frame-ancestors 指定）</li><li>セキュリティヘッダー (CSP, Referrer-Policy, X-Content-Type-Options, Strict-Transport-Security, X-XSS-Protection など)</li><li>ログ・例外情報の漏えい防止（スタックトレース抑制、詳細情報非表示）</li><li>セッション管理・Cookie 設定（Secure, HttpOnly, SameSite 属性など）</li><li>API セキュリティ強化策（Rate Limit, CORS 制御, API キー / OAuth, 認可層の強化）</li></ol> |
| 第 13 章 | 非同期処理と並行性設計（予定） | <ol><li>async / await の基礎</li><li>CancellationToken の概念と使い方</li><li>非同期／同期混在時の注意点</li><li>並行処理／並列化 (Tasks, Parallel)</li><li>スレッドプール枯渇を避ける設計</li><li>バックグラウンド処理 (IHostedService, BackgroundService)</li></ol> |
| 第 14 章 | 外部サービス呼び出し：HttpClient と API 通信（予定） | <ol><li>HttpClient の基本使い方</li><li>HttpClient のライフサイクル管理問題</li><li>IHttpClientFactory を使ったクライアント管理（Named / Typed Clients）</li><li>Delegating Handler による共通処理（ログ・認証付与・リトライなど）</li><li>Polly 等を使ったレジリエンス設計</li><li>タイムアウト・非同期呼び出し・エラーハンドリング</li><li>API 呼び出し部分をサービス層に抽象化し、テスト可能に設計</li><li>メール送信の実装方法</li></ol> |
| 第 15 章 | キャッシュ / セッション / Cookie / 状態管理（予定） | <ol><li>メモリキャッシュ / 分散キャッシュ (Redis 等)</li><li>出力キャッシュ / レスポンスキャッシュ</li><li>セッションと Cookie 管理</li><li>ETag / キャッシュ制御ヘッダー</li><li>キャッシュ戦略設計上の注意点</li></ol> |
| 第 16 章 | バックエンドタスク / バックグラウンド処理（予定） | <ol><li>背景処理 (Background Tasks) の意義と適用シナリオ<ul><li>定期処理 (CRON 相当)、バッチ処理、キュー処理、ポーリング、非同期ジョブなど</li></ul></li><li>IHostedService / BackgroundService による実装</li><li>スコープ付きサービスとの併用<ul><li>バックエンド処理内で Scoped なサービス (例：DbContext 等) を使うための IServiceScope 利用法</li></ul></li><li>キューベース / 非同期タスクキュー処理</li><li>エラーハンドリング / リトライ制御 / 再実行戦略</li><li>停止・シャットダウン時の処理<ul><li>StopAsync / キャンセル通知 / グレースフルシャットダウン設計</li></ul></li><li>Web アプリとの共存時の注意点<ul><li>アイドルスリープ環境、Always On 設定、アプリ停止対策など</li></ul></li><li>外部 API 呼び出しとの組み合わせ<ul><li>バックエンド処理で HttpClient / IHttpClientFactory を使う際の注意点</li></ul></li><li>補足：3rd Party バックグラウンドフレームワーク（Hangfire 等）<ul><li>軽い紹介レベルで、Hangfire などのライブラリを使う選択肢について触れる</li></ul></li><li>実装パターン比較と注意点<ul><li>長時間 vs 短時間処理、ジョブ粒度設計、同時実行制御、テスト方針など</li></ul></li><li>サンプルテンプレート / コードスニペット</li></ol> |
| 第 17 章 | パフォーマンス最適化 / スケーラビリティ（予定） | <ol><li>ボトルネックの特定 (プロファイラー / ログ / Application Insights 等)</li><li>キャッシュ活用・遅延ロード制御</li><li>DB 接続最適化 / プール設定</li><li>非同期最適化 / 中断可能処理</li><li>圧縮 / 静的ファイル最適化 / CDN 利用</li><li>スケールアウト設計 (ステートレス化, セッション戦略, 分割設計)</li></ol> |
| 第 18 章 | ミドルウェアと HTTP リクエストパイプライン（予定） | <ol><li>ミドルウェア (Middleware) の概念</li><li>リクエストの通り道：UseRouting → UseAuthentication → UseAuthorization → UseEndpoints など</li><li>例外処理ミドルウェア / ログミドルウェアの実装例</li><li>Forwarded Headers (X-Forwarded-For, X-Forwarded-Proto 等) の扱い<ul><li>UseForwardedHeaders と ForwardedHeadersOptions の設定</li><li>信頼できるプロキシ (KnownProxies / KnownNetworks) の指定</li><li>CDN／リバースプロキシ構成におけるクライアント IP の取得戦略</li><li>ミドルウェア適用順序と認証・URL 生成への影響</li></ul></li></ol> |
| 付録 | 付録（予定） | <ol><li>.NET CLI コマンドリファレンス</li><li>EF / LINQ クイックリファレンス</li><li>HttpClient / Polly サンプルテンプレート</li><li>よくあるエラーと対処</li><li>他言語との用語対照表</li><li>推奨開発パターン集</li></ol> |

---

## はじめに

### このコンテンツの対象読者・前提条件

本コンテンツは **C# 以外の開発言語（Java、Python、TypeScript／JavaScript、Go など）を用いた Web アプリケーション開発の経験がある方** を対象に、ASP.NET Core による Web アプリケーション・Web API 開発の基礎から実践的な活用方法までを習得していただくことを目的としています。そのため、HTTP の基礎的な知識、Web フレームワークの一般的な機能や設計パターンに関する知識があることを前提としています。

#### 前提条件

- 何らかのプログラミング言語でのアプリケーション開発経験がある
- 何らかの Web フレームワーク（Spring Boot、Django、Express.js、Gin など）を使用した経験がある
- HTTP の基本（リクエスト／レスポンス、ステータスコード、REST の概念）を理解している
- ターミナル（コマンドライン）の基本的な操作ができる

### 本ガイドで扱わない内容

本ガイドでは **Razor Pages** および **Blazor** の解説は行いません。  
ASP.NET Core には複数の Web UI フレームワークが含まれていますが、本ガイドでは **MVC パターンによる Web アプリケーション／Web API** と **Minimal API** に焦点を当てます。  
Razor Pages や Blazor に興味がある方は、以下の公式ドキュメントを参照してください。

- [Razor Pages の概要 - Microsoft Learn](https://learn.microsoft.com/aspnet/core/razor-pages/?view=aspnetcore-10.0)
- [Blazor の概要 - Microsoft Learn](https://learn.microsoft.com/aspnet/core/blazor/?view=aspnetcore-10.0)

また、C# の解説も基本的に行いませんが、必要に応じて特定機能の解説をする場合があります。C# を全く知らない場合はマイクロソフト公式の初心者向けのチュートリアルから始めることをおすすめします。

- [C# 言語のドキュメント](https://learn.microsoft.com/ja-jp/dotnet/csharp/)

### 本ガイドで扱うバージョン

| コンポーネント | バージョン | サポート種別 | サポート期限 |
|---|---|---|---|
| **.NET** | 10 | LTS (Long Term Support) | 2028 年 11 月 |
| **ASP.NET Core** | 10 | LTS | 2028 年 11 月 |
| **EF Core** | 10 | LTS | 2028 年 11 月 |

.NET 10 は 2025 年 11 月にリリースされた **長期サポート (LTS) リリース** であり、**3 年間のサポート**（2028 年 11 月まで）が提供されます。  
.NET は偶数バージョン（.NET 8, 10, 12…）が LTS、奇数バージョン（.NET 9, 11…）が STS (Standard Term Support / 18 か月) となるリリースサイクルを採用しています。

> **参照**: [What's new in .NET 10 - Microsoft Learn](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-10/overview)  
> **参照**: [Releases and support for .NET - Microsoft Learn](https://learn.microsoft.com/dotnet/core/releases-and-support)

### 開発スタイル（CLI + IDE 両対応）

本コンテンツでは、可能な限り以下の **3 つの開発環境での操作を併記** します。お使いの環境や好みに合わせて選択してください。

#### .NET CLI（コマンドライン）

[.NET CLI](https://learn.microsoft.com/dotnet/core/tools/dotnet) は .NET SDK に含まれるクロスプラットフォームのコマンドラインツールチェーンです。  
プロジェクトの作成・ビルド・実行・テスト・発行をすべてターミナルから行えます。  
OS やエディタを問わず同じコマンドで操作できるため、**どの環境でも再現性のある手順** を提供します。

#### Visual Studio

Windows 環境での .NET 開発において最も包括的な IDE です。デバッグ・テスト・プロファイリング・デプロイまで統合された開発体験を提供します。

#### Visual Studio Code + C# Dev Kit

軽量でクロスプラットフォーム対応のエディタである Visual Studio Code と、Microsoft が提供する C# 開発用拡張機能 C# Dev Kit を組み合わせることで、Windows・macOS・Linux いずれの環境でも快適に .NET 開発が可能です。.NET CLI と組み合わせて使用することが推奨されます。

> **参照**: [Development process for Azure - Microsoft Learn](https://learn.microsoft.com/dotnet/architecture/modern-web-apps-azure/development-process-for-azure#development-environment-for-aspnet-core-apps)

> [!TIP]
> .NET の IDE（統合開発環境）は他にも JetBrains 社の Rider や OSS として公開されているものもあります。
> 
---

## 皆さんの貢献をお待ちしています

このコンテンツはオープンソースで運営されており、皆さんからのフィードバックやプルリクエストを歓迎しています。コンテンツの不備への指摘、新コンテンツの提案のみならず、.NET のバージョンアップ時の内容更新なども大歓迎です。

貢献の具体的な方法については、[CONTRIBUTING.md](CONTRIBUTING.md) をご覧ください。

---

ほとんどの貢献には、貢献内容を使用する権利をマイクロソフトに付与する権利を有し、実際に付与することを表明する Contributor License Agreements（CLA）への同意が必要です。詳細については、[貢献者ライセンス契約](https://opensource.microsoft.com/cla/)をご覧ください。

プルリクエストを送信すると、CLAボットが自動的にCLAの提出が必要かどうかを判断し、プルリクエストに適切な情報（ステータスチェック、コメントなど）を追加します。ボットの指示に従ってください。この操作は、マイクロソフトのCLAを使用しているすべてのリポジトリで一度だけ行う必要があります。

このプロジェクトは、マイクロソフトの[オープンソース行動規範](https://opensource.microsoft.com/codeofconduct/) を採用しています。詳細については、[行動規範に関するよくある質問（FAQ）](https://opensource.microsoft.com/codeofconduct/faq/) をご覧ください。ご質問やご意見がございましたら、 [opencode@microsoft.com](mailto:opencode@microsoft.com)までお問い合わせください。


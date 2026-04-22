# ASP.NET Core ハンドブック

## はじめに

### このコンテンツの対象読者・前提条件

本コンテンツは **C# 以外の開発言語（Java、Python、TypeScript／JavaScript、Go など）の経験がある方** を対象に、ASP.NET Core による Web アプリケーション・Web API 開発の基礎を習得していただくことを目的としています。そのため、HTTP の基礎的な知識、Web フレームワークの一般的な機能や設計パターンに関する知識があることを前提としています。

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

### ASP.NET Core の概要・強み

[ASP.NET Core](https://learn.microsoft.com/aspnet/core/overview?view=aspnetcore-10.0) は、[.NET](https://learn.microsoft.com/dotnet/core/introduction) 上で動作する **クロスプラットフォーム・高パフォーマンス・オープンソース** の Web アプリケーションフレームワークです。  
Windows・macOS・Linux のいずれでも動作し、大規模なエンタープライズアプリケーションからシンプルなマイクロサービスまで幅広い規模のワークロードに対応できます。

#### 主な特長

| 特長 | 内容 |
|---|---|
| **統合フレームワーク** | Web 開発に必要なコンポーネントがすべて組み込まれた、完全に統合されたフレームワーク |
| **セキュア・バイ・デザイン** | 認証・認可・データ保護のセキュリティ機能を標準搭載 |
| **高パフォーマンス** | 軽量でモジュール化された HTTP リクエストパイプラインと、クロスプラットフォーム対応の高速 HTTP サーバ [Kestrel](https://learn.microsoft.com/aspnet/core/fundamentals/servers/kestrel?view=aspnetcore-10.0) を搭載。業界トップクラスの処理性能を発揮 |
| **クラウド対応** | オンプレミスでもクラウドでも、デプロイ・監視・構成がシンプル |
| **組み込み DI** | [依存性の注入 (Dependency Injection)](https://learn.microsoft.com/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-10.0) が標準機能として統合済み |
| **柔軟な構成管理** | `appsettings.json`、環境変数、コマンドライン引数など多様な[構成プロバイダ](https://learn.microsoft.com/aspnet/core/fundamentals/configuration/?view=aspnetcore-10.0)を利用可能 |
| **実績と信頼性** | Bing・Xbox・Microsoft 365・Azure など世界最大規模のサービスで採用・実証済み |

> **参照**: [Overview of ASP.NET Core - Microsoft Learn](https://learn.microsoft.com/aspnet/core/overview?view=aspnetcore-10.0)

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

## 目次

| 章 | タイトル | 内容 |
|---|---|---|
| 第 1 章 | [開発環境セットアップ](docs/01-setup-dev-env.md) | .NET SDK のインストール、IDE 準備、CLI 基本コマンド |
| 第 2 章 | [ソリューションとプロジェクト](docs/02-solutions-and-projects.md) | ソリューション・プロジェクト構成、マルチプロジェクト管理 |
| 第 3 章 | [MVC Web アプリケーション / API](docs/03-mvc-web-and-api.md) | MVC パターンによる Web アプリおよび API の実装 |
| 第 4 章 | [Minimal API](docs/04-minimal-api.md) | Minimal API によるシンプルな API 構築手法 |

## 皆さんの貢献をお待ちしています

このコンテンツはオープンソースで運営されており、皆さんからのフィードバックやプルリクエストを歓迎しています。コンテンツの不備への指摘、新コンテンツの提案のみならず、.NET のバージョンアップ時の内容更新なども大歓迎です。

ほとんどの貢献には、貢献内容を使用する権利を当社に付与する権利を有し、実際に付与することを表明する Contributor License Agreements（CLA）への同意が必要です。詳細については、[貢献者ライセンス契約](https://opensource.microsoft.com/cla/)をご覧ください。

プルリクエストを送信すると、CLAボットが自動的にCLAの提出が必要かどうかを判断し、プルリクエストに適切な情報（ステータスチェック、コメントなど）を追加します。ボットの指示に従ってください。この操作は、当社のCLAを使用しているすべてのリポジトリで一度だけ行う必要があります。

このプロジェクトは、マイクロソフトの[オープンソース行動規範](https://opensource.microsoft.com/codeofconduct/) を採用しています。詳細については、[行動規範に関するよくある質問（FAQ）](https://opensource.microsoft.com/codeofconduct/faq/) をご覧ください。ご質問やご意見がございましたら、 [opencode@microsoft.com](mailto:opencode@microsoft.com)までお問い合わせください。

貢献の具体的な方法については、[CONTRIBUTING.md](CONTRIBUTING.md) をご覧ください。
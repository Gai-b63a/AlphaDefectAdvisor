# AlphaDefect Advisor

Slay the Spire 2 のゲーム画面内に、ソロのアイアンクラッド向けの助言を日本語で表示します。ゲーム内の助言パネルには推薦行動と、最大3候補のモデル選択確率が出ます。カードや対象の選択、ターン終了はプレイヤーが操作します。Mod v0.2.2 の対象は Steam の public-beta v0.114.0 です。パネルは最初は右中央に表示され、見出しを左ドラッグして移動できます。離した位置は次回の起動にも使われます。開発版では Apple Silicon 用の推論環境もビルドしています。

## 初回の導入

Steam ワークショップで購読し、ダウンロードが終わったら Steam からゲームを起動します。初回はゲーム内に準備状況が表示されます。Mod は同梱の専用 Python、CPU 推論ライブラリ、公式 weights-v6 の重みを展開し、固定リビジョンの語彙表など5ファイルを GitHub から取得して SHA-256 で照合します。初回準備中はインターネット接続が必要です。完了後はソロのアイアンクラッドでランを開始してください。

推論環境は利用者ごとの `%LOCALAPPDATA%/AlphaDefectAdvisor/runtime` に作成します。モデルの手動取得、Python・Git の事前インストール、セットアップスクリプトや別アプリの起動は不要です。同じ配布版を再起動すると、照合済みのデータを再利用します。

通常の戦闘、マップ、イベント、休憩所、商店、報酬、カード報酬が対象です。v0.2.4 では「発見」のカード選択と、休憩所で鍛冶を選んだ後の強化対象にも助言を表示します。「発見」でスキップできるときはスキップも候補です。敵ターンや未対応の選択画面では待機します。詳細な実機受け入れ条件は開発リポジトリの REQUIREMENTS.md に記載しています。

## 開発用ビルド

開発者は .NET 9 SDK、対応ゲーム版、Windows x64 の標準 CPython 3.12 と pip、Visual Studio の x64 再頒布用 CRT、公式 weights-v6 の `ironclad_ema_v24782.pt` を用意し、重みをリポジトリ直下に置きます。ビルド時に CPU 用 wheel をハッシュ付きの一覧から取得します。Windows では「Steam Workshop」の `upstream-source-<コミットSHA>` を `upstream` に展開してから実行してください。

```powershell
$game = 'E:\SteamLibrary\steamapps\common\Slay the Spire 2'
.\tools\Build-Advisor.ps1 -GameAssemblyDir "$game\data_sts2_windows_x86_64"
.\tools\Install-Advisor.ps1 -GameDir $game
```

`tools/Build-Advisor.ps1` は `dist/workshop` に投稿用ワークスペースを作ります。ビルド用 Python は `-BuildPython`、取得済みの wheel は `-Wheelhouse`、CRT の場所は `-CrtDir` で指定できます。

## Windows・macOS の VM で試す（開発者向け）

**配布候補を試す場合**　GitHub Actions の「Steam Workshop」で同じコミットの Windows と macOS の `vm-runtime-test` の結果を確認します。両ジョブでは初回準備の失敗と復帰、オフラインでの再起動、既知シード4盤面の推奨・上位候補・確率を検証します。Steam のゲーム画面は別途確認します。

1. 開発用 Windows PC でこのリポジトリの対象コミットをチェックアウトし、対応ゲームを用意します。同じ Actions 実行の `upstream-source-<コミットSHA>` をリポジトリ直下の `upstream` に展開し（`upstream/engine/Sts2Headless` があること）、`workshop-runtime-<コミットSHA>` を別フォルダーに展開してください。
2. 開発用 PC で次のコマンドを実行します。`-RuntimeArtifactDir` に `runtime.json` と `runtime.zip` のあるフォルダーを渡すと、コミットとハッシュの一致を確認して DLL を作ります。両 OS の VM で同じ `dist/AlphaDefectAdvisor` を使います。

```powershell
.\tools\Build-Advisor.ps1 -GameAssemblyDir "$game\data_sts2_windows_x86_64" -RuntimeArtifactDir C:\AdvisorTest\artifact
.\tools\Test-WorkshopPackage.ps1 -Workspace .\dist\workshop
```

3. Windows 11 x64 と Apple Silicon の macOS ゲストに Steam とゲーム v0.114.0 を入れます。共有フォルダーを切り離してゲーム単体の描画・入力を確認し、Mod 未導入・未購読の状態を復元できるよう保存します。未公開候補の動作確認はその複製に配布フォルダーをゲームの `mods` 配下へコピーして行います。
4. **購読試験**は未購読の状態に戻してから実施します。試験対象の版が Workshop に公開された後、ゲスト内の Steam で購読し、ゲームを起動します。初回の助言、全7場面、通信遮断と復帰の合格条件・記録項目は [要件と VM 試験表](REQUIREMENTS.md) に従ってください。候補を手動配置した結果は、購読からの導入試験とは別に記録します。

## Steam ワークショップへの公開（メンテナー向け）

初回は Steam にログインし、検証済みの `dist/workshop` フォルダーを Windows のエクスプローラーから公式の `ModUploader.exe` にドラッグ＆ドロップして公開します。成功時に生成される `mod_id.txt` を、このリポジトリの `workshop/mod_id.txt` として `main` に登録してください。ファイルの中身は公開アイテムの数値 ID だけです。

Mod のバージョンを上げて main に反映すると GitHub Actions がリリースを作成します。Workshop に公開したいタイミングで、初回投稿に使った Windows ユーザーで `Update-Workshop.cmd` を実行してください。最新リリースを検証し、Mod をビルドして既存アイテム `3845626411` に一度送信します。公開用 PC では .NET 9 SDK、Python 3.11 以上、PowerShell 7、対応ゲーム版を使用します。

以前に定期タスクを登録した場合は、Windows のタスク スケジューラで `AlphaDefect Advisor Workshop Update` を無効にしてください。送信結果は `%LOCALAPPDATA%/AlphaDefectAdvisor/publisher/update.log` に記録します。送信結果が不明な場合は、もう一度 `Update-Workshop.cmd` を実行してください。前回のビルドと対象アイテムに対応する SteamCMD の完了ログがあれば、再送せず公開済みバージョンを記録して終了します。確認できない場合は送信を保留するので、Workshop の変更履歴を確認してください。

[公式 ModUploader](https://github.com/megacrit/sts2-mod-uploader/releases)

## ライセンス

自作部分の許諾は LICENSE、AlphaDefect 由来の許諾と適用範囲は配布物の AlphaDefect-LICENSE.txt と AlphaDefect-NOTICE.md に記載しています。ゲーム本体、抽出画像、ゲーム由来の語彙表やカタログは配布物に含めません。Slay the Spire 2 は Mega Crit のゲームです。本 Mod は有志によるものです。

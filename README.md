# AlphaDefect Advisor

Slay the Spire 2 のゲーム画面内に、ソロのアイアンクラッド向けの助言を日本語で表示します。画面左上のパネルには推薦行動と、最大3候補のモデル選択確率が出ます。カードや対象の選択、ターン終了はプレイヤーが操作します。対象は Windows の Steam 版 v0.113.1 です。

## 初回の導入

Steam ワークショップで購読し、ダウンロードが終わったら Steam からゲームを起動します。初回はゲーム内に準備状況が表示されます。Mod は同梱の専用 Python、CPU 推論ライブラリ、公式 weights-v6 の重みを展開し、固定リビジョンの語彙表など5ファイルを GitHub から取得して SHA-256 で照合します。初回準備中はインターネット接続が必要です。完了後はソロのアイアンクラッドでランを開始してください。

推論環境は利用者ごとの `%LOCALAPPDATA%/AlphaDefectAdvisor/runtime` に作成します。モデルの手動取得、Python・Git の事前インストール、セットアップスクリプトや別アプリの起動は不要です。同じ配布版を再起動すると、照合済みのデータを再利用します。

通常の戦闘、マップ、イベント、休憩所、商店、報酬、カード報酬が対象です。敵ターン、カードの追加選択、未対応画面では待機します。詳細な実機受け入れ条件は開発リポジトリの REQUIREMENTS.md に記載しています。

## 開発用ビルド

開発者は .NET 9 SDK、対応ゲーム版、Windows x64 の標準 CPython 3.12 と pip、Visual Studio の x64 再頒布用 CRT、公式 weights-v6 の `ironclad_ema_v24782.pt` を用意し、重みをリポジトリ直下に置きます。ビルド時に CPU 用 wheel をハッシュ付きの一覧から取得します。

```powershell
git clone https://github.com/hmty-CHEN/AlphaDefect-Public.git upstream
git -C upstream checkout --detach 800db3039162647bf0ad13ed379f155cc5806f5f
$game = 'E:\SteamLibrary\steamapps\common\Slay the Spire 2'
.\Build-Advisor.ps1 -GameAssemblyDir "$game\data_sts2_windows_x86_64"
.\Install-Advisor.ps1 -GameDir $game
```

`Build-Advisor.ps1` は `dist/workshop` に投稿用ワークスペースを作ります。ビルド用 Python は `-BuildPython`、取得済みの wheel は `-Wheelhouse`、CRT の場所は `-CrtDir` で指定できます。

## Steam ワークショップへの公開（メンテナー向け）

1. GitHub Actions の **Prepare Steam Workshop release** を実行します。手動実行と `workshop-v*` タグに対応します。Windows ランナーが公式モデルと固定版の依存ファイルを取得し、検証済みの `runtime.zip` とコミット・SHA-256 を記録した `runtime.json` を成果物に保存します。
2. Steam にログインした Windows PC で、成果物と同じコミットをチェックアウトします。開発用ビルドと同じリビジョンの `upstream`、ゲーム v0.113.1、.NET 9 SDK、PowerShell 7、検証用の Python 3.12 を用意します。Actions の成果物を展開し、次を実行します。

   ```powershell
   .\Build-Advisor.ps1 -GameAssemblyDir "$game\data_sts2_windows_x86_64" -RuntimeArtifactDir 'C:\release\artifact'
   .\Publish-Workshop.ps1 -Mode Check
   ```

   ローカルでランタイムも含めてビルドした場合は、開発用ビルドの後に同じ `Check` を実行できます。投稿前に、対象コミットの配布物で REQUIREMENTS.md の実機受け入れ条件を確認してください。

3. 初回だけ、Mega Crit 公式アップローダーの実行ファイルを指定して投稿します。成功時に表示された Steam ページを確認し、生成された `workshop/mod_id.txt` をコミットして、今後の更新先を共有します。

   ```powershell
   .\Publish-Workshop.ps1 -Mode Create -Uploader 'C:\ModUploader\ModUploader.exe'
   ```

4. 更新時は `workshop/workshop.json` の `changeNote` と Mod のバージョンを更新して再ビルドし、同じアイテムに投稿します。

   ```powershell
   .\Publish-Workshop.ps1 -Mode Update -Uploader 'C:\ModUploader\ModUploader.exe'
   ```

初回投稿が途中で止まった場合は、Steam の「自分のワークショップファイル」とアップローダーの `mod-uploader.log` を確認してください。作成済みなら `-Mode Update -ItemId <公開ID>` で続行できます。作成されていないことを確認できた場合に限り、`workshop/.create-pending` を削除して初回投稿を再実行します。Steam への送信は `Create` または `Update` を実行したときに行われます。

[公式 ModUploader](https://github.com/megacrit/sts2-mod-uploader/releases)

## ライセンス

自作部分の許諾は LICENSE、AlphaDefect 由来の許諾と適用範囲は配布物の AlphaDefect-LICENSE.txt と AlphaDefect-NOTICE.md に記載しています。ゲーム本体、抽出画像、ゲーム由来の語彙表やカタログは配布物に含めません。Slay the Spire 2 は Mega Crit のゲームです。本 Mod は有志によるものです。

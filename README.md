# I Am Cat Gojo Mod

I Am Cat向けの非公式MelonLoader MODです。五条悟風の能力をVR操作で使えるようにします。

> This is an unofficial fan-made mod. It is not affiliated with New Folder Games, I Am Cat, Jujutsu Kaisen, or their respective rights holders.  
> このリポジトリには、ゲーム本体のファイル、Unity/MelonLoader由来DLL、アニメ/ゲーム由来アセットは含めません。

## Features

- **Infinity / 無下限**  
  猫の周囲に青いバリアを出し、投げ物や物理オブジェクトを減速させます。
- **Blue / 蒼**  
  前方に青い吸引球を出し、周囲の物体やNPCを吸い込みます。
- **Red / 赫**  
  前方に赤い衝撃波を放ち、物体やNPCを吹き飛ばします。
- **Purple / 茈**  
  赤と青の球を合体させて紫の弾を撃ち、触れた小物を消滅表現します。
- **Domain Expansion / 領域展開**  
  周囲を白/黒の空間に切り替え、NPCや物体を停止・石化させる動画向け大技です。
- **Teleport / 瞬間移動**  
  右グリップで視線方向へ短距離移動します。壁抜けしにくいように衝突チェックを入れています。
- **VR Wheel Menu**  
  B長押しでVR内ホイールメニューを開き、能力を選択できます。

## Controls

| 操作 | 内容 |
| --- | --- |
| B 短押し | 能力を順番に切り替え |
| B 長押し | ホイールメニュー表示、離すと選択確定 |
| 右トリガー | 選択中の能力を発動 |
| 右グリップ | 瞬間移動 |
| A 長押し | 領域展開 |
| 左トリガー | 発動中の能力/演出をキャンセル |

## Requirements

- Windows / PCVR環境
- Steam版 **I Am Cat**
- MelonLoader 0.7.x系のIL2CPP/Net6環境
- .NET 6 SDK
- Visual Studio 2022 または `dotnet` CLI

動作確認は開発者環境依存です。ゲーム本体やMelonLoaderのアップデートで動かなくなる可能性があります。

## Build

このリポジトリには、ゲーム本体から生成される参照DLLやUnity DLLは含めません。ビルド時は自分のPCにあるI Am Catのインストール先を指定してください。

### 方法1: コマンドで直接指定

```powershell
dotnet build .\GojoMOD.csproj -c Release -p:Platform=x64 -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\I Am Cat"
```

### 方法2: 環境変数で指定

```powershell
$env:I_AM_CAT_GAME_DIR = "C:\Program Files (x86)\Steam\steamapps\common\I Am Cat"
dotnet build .\GojoMOD.csproj -c Release -p:Platform=x64
```

### 方法3: ローカルpropsを使う

`Directory.Build.props.example` を `Directory.Build.props` にコピーして、`GameDir`を書き換えます。
`Directory.Build.props` は `.gitignore` 済みなのでコミットしないでください。

```powershell
copy .\Directory.Build.props.example .\Directory.Build.props
notepad .\Directory.Build.props
dotnet build .\GojoMOD.csproj -c Release -p:Platform=x64
```

ビルドに成功すると、`IamCat.GojoMod.dll` が生成されます。`GameDir` が正しく設定されている場合は、ビルド後に `<I Am Cat>/Mods` へ自動コピーされます。

## Install

ReleaseからDLLを使う場合は、以下に配置してください。

```text
<I Am Cat>/Mods/IamCat.GojoMod.dll
```

ゲーム起動後、MelonLoaderログに `I Am Cat Gojo Mod initialized` が出れば読み込み成功です。

## Optional: Domain Expansion background image

領域展開の黒背景を差し替えたい場合は、任意の画像を以下に置けます。

```text
<I Am Cat>/UserData/IAmCatGojoMod/domain_blackhole_background.png
```

またはRGBA bytes版:

```text
<I Am Cat>/UserData/IAmCatGojoMod/domain_blackhole_background_1024x576_rgba.bytes
```

画像素材の権利が不明な場合は、リポジトリにはコミットせず、各自のローカル環境やRelease Assetsで扱ってください。

## Repository policy

コミットしてよいもの:

- `*.cs` ソースコード
- `*.csproj`
- README / CHANGELOG / LICENSE / `.gitignore`
- サンプル設定ファイル `.example`

コミットしないもの:

- `bin/`, `obj/`, `.vs/`
- `*.dll`, `*.pdb`, `*.deps.json`
- `MelonLoader/`, `Mods/`, `UserData/`, `Logs/`
- ゲーム本体から生成された `Il2CppAssemblies` 内のDLL
- Unityやゲーム由来のアセット
- 権利が不明な画像、モデル、音声、AssetBundle
- 自分のPCの絶対パスを含む設定ファイル

配布用のDLLやZIPは、GitHubの通常コミットではなく **GitHub Releases** にアップロードする運用がおすすめです。

## Development notes

このMODは動画映えを優先した実験的なコードです。NPC制御、物理、Renderer非表示、VR入力などゲーム内部に強く依存します。

既知の方針:

- 古い同名クラスの `.cs` ファイルを同時に置かない
- ゲーム/Unity/MelonLoader由来DLLは同梱しない
- 大きい画像やAssetBundleはコミットしない
- 動かなかった場合はMelonLoaderログを確認する

## License

Source code is licensed under the MIT License. See [LICENSE](./LICENSE).

This license applies only to the source code in this repository. It does not grant rights to I Am Cat, Jujutsu Kaisen, Unity, MelonLoader, or any third-party assets/trademarks.

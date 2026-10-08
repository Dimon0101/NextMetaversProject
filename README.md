# NextMetaversProject — інтерактивна лекційна зала у VRChat

Світ VRChat для NEXT-Study Metaverse: лекційна зала ФІОТ КПІ з інтерактивною дошкою.
На дошку виводяться відео за посиланням, презентації та слайди з проєкту; у кожного з 48 крісел є особистий планшет із тим самим зображенням зблизька; поверх зображення можна малювати. Усе синхронізовано між гравцями, права керування видає головний стіл на кафедрі.

- Unity **2022.3.22f1**
- VRChat Worlds SDK **3.10.4** (Udon + UdonSharp), підключений через VPM
- Платформи: **PC (Windows)** і **Android (Quest)** з однієї сцени
- Головна сцена: `Assets/Scenes/VRCDefaultWorldScene.unity`

Детальний опис самої системи дошки — в [Assets/PresentationBoard/README.md](Assets/PresentationBoard/README.md).

## Швидкий старт

1. Відкрити проєкт у Unity 2022.3.22f1 (через VRChat Creator Companion або напряму).
2. Відкрити `Assets/Scenes/VRCDefaultWorldScene.unity`.
3. Play Mode (ClientSim) — дошка, планшети й малювання працюють одразу.
4. Якщо об'єкти дошки зламалися або їх немає: меню **NextMetavers → Дошка → Зібрати дошку в сцені**.

Меню **NextMetavers → Дошка**:

| Пункт | Що робить |
|---|---|
| Зібрати дошку в сцені | Перестворює дошку, пульт кафедри, планшети крісел і область малювання |
| Оновити слайди з папки | Перечитує `Assets/PresentationBoard/Slides` у список слайдів дошки |
| Оновити yt-dlp | Завантажує/оновлює `Library/PresentationBoard/yt-dlp.exe` для YouTube у Play Mode |
| Перевірити дошку | Проганяє набір автотестів у Play Mode (124 перевірки на PC, 120 на Android) |

## Де що лежить

### Наш код і контент

| Шлях | Що це |
|---|---|
| `Assets/Scenes/VRCDefaultWorldScene.unity` | Головна сцена світу: зала, освітлення, дошка, крісла, VRCWorld |
| `Assets/Scenes/VRCDefaultWorldScene/` | Матеріали й текстури, що належать сцені (підлога, небо, дзеркало) |
| `Assets/Model/Room_ready.fbx` | Модель зали з Blender: стіни, вікна, двері, подіум, стеля |
| `Assets/Model/*.mat` | Матеріали моделі: `Background`, `Glass`, `LampGlow` |
| `Assets/PresentationBoard/` | Уся система дошки (код, матеріали, слайди, рендер-текстури) |
| `Assets/Textures/` | Текстури залу й банерів |
| `Assets/XR/` | Налаштування XR (Oculus loader) для збірки під шолом |

### Система дошки — `Assets/PresentationBoard/`

| Файл | Призначення |
|---|---|
| `README.md` | Повна інструкція: режими, права, малювання, звук, YouTube, PC/Android, тести |
| `Scripts/PresentationBoard.cs` | Ядро: режими Off/Video/Slides/VideoSlides, синхронізація (`_mode`, `_url`, `_startServerTime`, `_slide`, `_hostId`, `_requestId`, `_openAccess`), вибір плеєра під платформу, права ведучого, запити й відкликання прав |
| `Scripts/BoardDrawing.cs` | Малювання: 6 інструментів (пензель, лінія, прямокутник, еліпс, стрілка, гумка), 3 товщини, 6 кольорів, ліміт 120 штрихів / 1500 точок, синхронізація штрихів масивами |
| `Scripts/PersonalTablet.cs` | Велика панель планшета: показ, ховання, прив'язка до крісла (локально, без синхронізації) |
| `Scripts/SeatTablet.cs` | Натиск на планшет крісла → відкрити панель |
| `Scripts/SeatStation.cs` | Посадка в крісло (VRCStation) → показати планшет, вставання → сховати |
| `Scripts/*.asset` | Згенеровані UdonSharp program assets для скриптів вище |
| `Editor/PresentationBoardBuilder.cs` | Генератор сцени: створює дошку, екрани, обидва відеоплеєри, динамік, пульт кафедри, панель планшета, 48 планшетів з якорями й станціями, область малювання з 120 LineRenderer |
| `Editor/EditorUrlResolver.cs` | Розв'язання YouTube-посилань у Play Mode через yt-dlp (у VRChat це робить сам клієнт) |
| `Editor/BoardTests.cs` | Автотести в Play Mode: стан, відео, слайди, планшети, права, малювання, крос-платформні налаштування |
| `Materials/` | Матеріали екранів і панелей: `BoardVideoScreen`, `BoardVideoScreenAVPro`, `BoardSlideScreen`, `BoardDrawScreen`, `BoardPanelBack`, `BoardStroke` |
| `Textures/BoardVideo.renderTexture` | Ціль Unity-відеоплеєра (PC) |
| `Textures/BoardDraw.renderTexture` | Шар малюнка, який камера області малювання віддає дошці й планшетам |
| `Slides/Demo01..03.png` | Демо-слайди, вбудовані у світ; свої картинки кладуться сюди ж |

### Зовнішні ассети (не наш код)

| Шлях | Що це |
|---|---|
| `Assets/UdonSharp/` | Утиліти UdonSharp із SDK |
| `Assets/SerializedUdonPrograms/` | Скомпільовані Udon-програми, Unity генерує їх сам |
| `Assets/LowPolyOfficeProps_LITE/` | Меблі й офісний реквізит (крісла, столи) |
| `Assets/NatureStarterKit2/` | Стандартні ефекти й матеріали оточення |
| `Assets/AllSkyFree/` | Набір скайбоксів |
| `Assets/Rune Assets/` | Прості будівлі для фону за вікнами |
| `Assets/_TerrainAutoUpgrade/` | Шари ландшафту, згенеровані при апгрейді |

### Службове

| Шлях | Що це |
|---|---|
| `Packages/vpm-manifest.json` | Версії VRChat SDK (Worlds 3.10.4) |
| `Packages/manifest.json`, `packages-lock.json` | Пакети Unity |
| `ProjectSettings/` | Налаштування проєкту: шари, теги, графіка, якість, XR |
| `ClientSimStorage/` | Локальні дані симуляції ClientSim (не впливають на світ) |
| `Library/`, `Logs/`, `Temp/` | Кеш Unity, у git не потрапляють |

## Збірка світу

1. **PC:** Build target → Windows, далі VRChat SDK → Build & Publish.
2. **Quest/Android:** потрібен модуль *Android Build Support*; Build target → Android і завантаження на **той самий ID світу**.

Скрипт сам обирає плеєр: на PC і в редакторі — Unity Video Player, у збірці під Android — AVPro (`#if UNITY_ANDROID`).

## Тести

З редактора: **NextMetavers → Дошка → Перевірити дошку**.

З командного рядка (Unity має бути закритий):

```
Unity.exe -batchmode -projectPath . -executeMethod NextMetavers.EditorTools.BoardTests.RunFromCommandLine -logFile tests.log
```

Код виходу 0 — усі перевірки пройшли, 1 — є помилки; подробиці в лозі за словом `[Tests]`.

## Документи проєкту

| Файл | Що це |
|---|---|
| `Docs/presentation-script.md` | Текст виступу до конкурсної презентації, по слайдах |
| `Docs/article.docx` | Стаття про проєкт за шаблоном конференції (IEEE A4) |

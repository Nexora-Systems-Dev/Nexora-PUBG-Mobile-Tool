# Nexora PUBG Mobile Tool — Project Layout

> هذه الوثيقة هي المخطط المعتمد لنقل الملفات وتنظيم المشروع فقط.
> لا تعني إعادة كتابة الخدمات أو تغيير السلوك الحالي أو إضافة طبقات غير ضرورية.
> أي نقل يجب أن يحافظ على نفس الـ namespaces والسلوك والاختبارات قدر الإمكان، ثم يتم تحديث الـ namespaces تدريجيًا بعد نجاح البناء والاختبارات.

## 1. الهدف

الهدف من هذا التنظيم هو جعل كل ميزة قابلة للفهم والتعديل والاختبار من مكان واحد، مع الحفاظ على:

- Dependency Injection الحالي في `App.xaml.cs`.
- حدود GameLoop وADB وRegistry والملفات الحساسة.
- الفصل بين واجهة WPF والمنطق التشغيلي.
- قابلية الاختبار بدون تشغيل GameLoop.
- عدم وجود مسارات GameLoop ثابتة أو عمليات Windows مبعثرة.
- عدم وجود طبقة عامة مكررة باسم `Linking operations to the interface`.
- عدم إنشاء أكثر من مالك لنفس المسؤولية.

## 2. الهيكل النهائي

```text
Nexora PUBG Mobile Tool c#/
│
├── App.xaml
├── App.xaml.cs
├── GlobalUsings.cs
├── MainWindow.xaml
├── MainWindow.xaml.cs
├── Nexora.csproj
├── Nexora.slnx
├── app.manifest
│
├── Bootstrap/
│   ├── ServiceCollectionExtensions.cs
│   └── StartupTasks.cs
│
├── Configuration/
│   ├── AppConstants.cs
│   ├── EmulatorOptions.cs
│   ├── GameLoopOptions.cs
│   ├── IpadLayoutOptions.cs
│   ├── TempCleanupOptions.cs
│   └── UpdateOptions.cs
│
├── Features/
│   ├── Graphics/
│   │   ├── Presentation/
│   │   │   ├── GraphicsView.xaml
│   │   │   ├── GraphicsView.xaml.cs
│   │   │   ├── GraphicsViewModel.cs
│   │   │   └── GraphicsDisplayFormatter.cs
│   │   ├── Application/
│   │   │   ├── IGraphicsSettingsService.cs
│   │   │   └── GraphicsSettingsService.cs
│   │   └── Domain/
│   │       ├── GraphicsModels.cs
│   │       ├── PubgVersionCatalog.cs
│   │       ├── ShadowSettingsStore.cs
│   │       ├── Ue4SavEditor.cs
│   │       └── UnrealCVarCodec.cs
│   │
│   ├── Optimizer/
│   │   ├── Presentation/
│   │   │   ├── OptimizerView.xaml
│   │   │   ├── OptimizerView.xaml.cs
│   │   │   ├── OptimizerViewModel.cs
│   │   │   └── OptimizerDisplayFormatter.cs
│   │   ├── Application/
│   │   │   ├── IPerformanceEngine.cs
│   │   │   └── PerformanceEngineFacade.cs
│   │   └── Domain/
│   │       ├── PerformanceModels.cs
│   │       ├── PerformancePlanBuilder.cs
│   │       └── PerformanceExecutionReport.cs
│   │
│   ├── Tuning/
│   │   ├── Presentation/
│   │   │   ├── TuningView.xaml
│   │   │   ├── TuningView.xaml.cs
│   │   │   └── TuningViewModel.cs
│   │   ├── Application/
│   │   │   ├── IEmulatorSettingsService.cs
│   │   │   └── EmulatorSettingsService.cs
│   │   └── Domain/
│   │       ├── EmulatorTuningCatalog.cs
│   │       └── EmulatorTuningModels.cs
│   │
│   ├── Network/
│   │   ├── Presentation/
│   │   │   ├── NetworkView.xaml
│   │   │   ├── NetworkView.xaml.cs
│   │   │   └── NetworkViewModel.cs
│   │   ├── Application/
│   │   │   ├── INetworkToolsService.cs
│   │   │   ├── NetworkToolsService.cs
│   │   │   ├── IIpadLayoutService.cs
│   │   │   └── IpadLayoutService.cs
│   │   └── Domain/
│   │       ├── DnsCatalog.cs
│   │       ├── IpadPresetCatalog.cs
│   │       └── IpadResolutionPreset.cs
│   │
│   ├── Shortcuts/
│   │   ├── Presentation/
│   │   │   ├── ShortcutsView.xaml
│   │   │   ├── ShortcutsView.xaml.cs
│   │   │   └── ShortcutsViewModel.cs
│   │   ├── Application/
│   │   │   ├── IShortcutService.cs
│   │   │   └── ShortcutService.cs
│   │   └── Domain/
│   │       └── ShortcutModels.cs
│   │
│   ├── About/
│   │   └── Presentation/
│   │       ├── AboutView.xaml
│   │       └── AboutView.xaml.cs
│   │
│   ├── GameLoop/
│   │   ├── Application/
│   │   │   ├── IGameLoopConnection.cs
│   │   │   ├── IGraphicsProfileStore.cs
│   │   │   ├── IAdbClient.cs
│   │   │   └── GameLoopService.cs
│   │   ├── Domain/
│   │   │   ├── GameLoopModels.cs
│   │   │   ├── GameLoopSession.cs
│   │   │   ├── GameLoopWorkingStorage.cs
│   │   │   └── RemotePaths.cs
│   │   └── Infrastructure/
│   │       ├── AdbClient.cs
│   │       ├── GameLoopConnector.cs
│   │       └── GraphicsSettingsApplier.cs
│   │
│   ├── Updates/
│   │   ├── Application/
│   │   │   └── IUpdateService.cs
│   │   ├── Domain/
│   │   │   └── UpdateInfo.cs
│   │   └── Infrastructure/
│   │       ├── UpdateService.cs
│   │       ├── UpdateChecker.cs
│   │       ├── UpdateArchiveValidator.cs
│   │       ├── UpdateHandoffBuilder.cs
│   │       └── StagingDirectoryGC.cs
│   │
│   ├── Security/
│   │   └── Infrastructure/
│   │       └── DefenderExclusionService.cs
│   │
│   └── Performance/
│       ├── Application/
│       │   ├── IGameLoopPerformanceEngine.cs
│       │   └── IGameLoopProcessService.cs
│       ├── Domain/
│       │   ├── PerformanceExecutionReport.cs
│       │   ├── PerformanceModels.cs
│       │   └── PerformancePlanBuilder.cs
│       └── Infrastructure/
│           ├── GameLoopProcessService.cs
│           ├── GameLoopRegistryOptimizer.cs
│           ├── GpuRoutingService.cs
│           ├── HardwareDetectionService.cs
│           ├── NvidiaOptimizerService.cs
│           ├── PerformanceEngineFacade.cs
│           ├── PowerSessionService.cs
│           ├── ProcessPriorityApplier.cs
│           ├── ProcessPriorityMonitor.cs
│           ├── ProcessPriorityService.cs
│           └── ProcessPrioritySnapshotStore.cs
│
├── Infrastructure/
│   ├── Files/
│   │   ├── PhysicalFileSystem.cs
│   │   └── FileUtilities.cs
│   ├── GameLoop/
│   │   ├── GameLoopPathResolver.cs
│   │   ├── GameLoopProcessEnumerator.cs
│   │   ├── GameLoopWorkRootProvider.cs
│   │   └── RegistryService.cs
│   ├── Processes/
│   │   ├── ProcessRunner.cs
│   │   └── ProcessText.cs
│   └── Registry/
│       ├── IUserRegistry.cs
│       └── IMachineRegistry.cs
│
├── UI/
│   ├── Behaviors/
│   │   └── WindowChromeBehavior.cs
│   ├── Controls/
│   │   └── [reusable WPF controls only]
│   ├── Layout/
│   │   └── ResponsiveLayoutManager.cs
│   ├── Presentation/
│   │   └── ActivityReportFormatter.cs
│   ├── Navigation/
│   │   └── NavigationItem.cs
│   ├── Helpers/
│   │   └── IconImageLoader.cs
│   └── Styles/
│       └── [optional resource dictionaries]
│
├── Shared/
│   ├── Contracts/
│   │   └── OperationResult.cs
│   └── Kernel/
│       ├── IFileSystem.cs
│       ├── IProcessRunner.cs
│       └── IWorkRootProvider.cs
│
├── Assets/
├── Nexora.Tests/
│   ├── Architecture/
│   ├── Features/
│   │   ├── Graphics/
│   │   ├── Optimizer/
│   │   ├── Tuning/
│   │   ├── Network/
│   │   ├── Shortcuts/
│   │   ├── GameLoop/
│   │   ├── Updates/
│   │   └── Performance/
│   ├── Infrastructure/
│   ├── UI/
│   └── TestDoubles/
│
├── scripts/
├── .github/
├── README.md
├── ARCHITECTURE.md
└── PROJECT-LAYOUT.md
```

## 3. قواعد الملكية وعدم التداخل

كل ملف يجب أن يملك مسؤولية واحدة ومكانًا واحدًا فقط.

| نوع الملف | مكانه الوحيد | ممنوع وضعه في |
|---|---|---|
| XAML الخاص بصفحة | `Features/<Feature>/Presentation` | `UI`, `Services`, `Operations` |
| ViewModel أو منسق عرض | `Features/<Feature>/Presentation` | `Shared`, `Infrastructure` |
| واجهة خدمة تخص ميزة | `Features/<Feature>/Application` | مجلد عام اسمه `Interfaces` |
| تنفيذ خدمة تخص ميزة | `Features/<Feature>/Application` أو `Infrastructure` حسب وجود IO | `MainWindow.xaml.cs` |
| نموذج وقواعد ميزة | `Features/<Feature>/Domain` | `UI`, `Shared` |
| Registry / Process / File / ADB | `Infrastructure` أو `Features/<Feature>/Infrastructure` | ViewModel أو XAML |
| خيار إعداد قابل للحقن | `Configuration` | داخل خدمة أو ViewModel |
| كود مشترك لا يعرف ميزة | `Shared` | داخل Feature عشوائي |
| اختبارات | `Nexora.Tests` بمسار مطابق للإنتاج | داخل المشروع الرئيسي |

لا يتم إنشاء مجلدات عامة مثل:

- `interface/`
- `Operations/`
- `Linking operations to the interface/`
- `Helpers/` يحتوي منطقًا تشغيليًا.
- `Utils/` للكود الذي لا نعرف مالكه.
- `Common/` كبديل عام لـ `Shared`.

هذه الأسماء تخفي الملكية وتؤدي إلى تكرار المسؤوليات.

## 4. اتجاه الاعتماديات

يجب أن تتحرك الاعتماديات في اتجاه واحد:

```text
Presentation
    ↓
Application
    ↓
Domain / Contracts
    ↓
Infrastructure
```

والقواعد التفصيلية هي:

1. `Presentation` لا يستدعي `Registry`, `Process`, `File`, `ADBD` أو `PowerShell` مباشرة.
2. `ViewModel` يعتمد على interfaces، وليس على implementations.
3. `Application` ينسق العملية ويطبق قواعد الاستخدام، لكنه لا يحتوي XAML.
4. `Domain` لا يعتمد على WPF أو Registry أو Process أو `MessageBox`.
5. `Infrastructure` ينفذ الوصول للنظام، ولا يقرر ماذا يفعل المستخدم في الواجهة.
6. `Shared` لا يعتمد على أي Feature.
7. Feature يمكنها استعمال contract مشترك، لكنها لا تصل إلى implementation داخلي لميزة أخرى.
8. `MainWindow` يبقى shell للتنقل وتنسيق lifecycle فقط، ولا يحتوي منطق Graphics أو Tuning أو Network.

## 5. قواعد GameLoop وWindows الحساسة

هذه الحدود إلزامية ولا يجوز تجاوزها عند النقل:

- كل مسار GameLoop يمر عبر `IGameLoopPathResolver`.
- لا تستخدم `Process.GetProcessesByName` خارج `GameLoopProcessEnumerator` أو `IGameLoopProcessService`.
- لا تكتب Registry مباشرة من View أو ViewModel.
- كل عملية ADB تمر عبر `IAdbClient`.
- كل تشغيل process يمر عبر `IProcessRunner`.
- كل ملف يمكن اختباره يمر عبر `IFileSystem` عندما يكون ذلك مناسبًا.
- `IGameLoopConnection` و`IGraphicsProfileStore` يجب أن يبقيا factory-forward إلى نفس singleton `GameLoopService`؛ لا تسجل تنفيذين منفصلين.
- `IpadLayoutService` يحتفظ بحارس GameLoop running-state والنسخ الاحتياطي والاستعادة.
- لا تُنقل أي قيمة حساسة أو مسار أو اسم Registry إلى XAML.
- العمليات التي تكتب Registry أو ملفات GameLoop أو إعدادات Windows تبقى user-triggered وواضحة في Application layer.

## 6. قواعد الثبات وAsync

- كل API غير متزامن يأخذ `CancellationToken` عندما تكون العملية قابلة للإلغاء.
- ممنوع استخدام `.Result` و`.Wait()` و`async void` إلا في event handlers الخاصة بـ WPF.
- لا يتم إنشاء `CancellationTokenSource` داخل كل طبقة بدون ملكية واضحة؛ الطبقة التي تنشئه تملكه وتتخلص منه.
- كل timeout يجب أن يكون محدودًا ومقصودًا، وليس loop مفتوحًا.
- يجب تنظيف resources وعمليات staging في `finally`.
- لا يتم تشغيل live GameLoop tests ضمن الاختبارات الافتراضية.
- أي فشل في اكتشاف GameLoop يجب أن يفشل برسالة واضحة، لا أن ينتقل إلى مسار تخميني خطير.

## 7. قواعد WPF والواجهة

- كل صفحة تصبح `UserControl` مستقلة داخل Feature الخاص بها.
- `MainWindow.xaml` يحتفظ بالـ shell والتنقل ومناطق العرض فقط.
- `MainWindow.xaml.cs` لا يحتوي منطقًا خاصًا بميزة كاملة.
- لا تضع business logic داخل event handler؛ استدعِ command أو ViewModel method.
- لا تنقل `Freezable` أو resource dictionaries إلى ViewModel.
- تنسيق الأرقام والنصوص يبقى في Formatter قابل للاختبار.
- حسابات layout تبقى في `UI/Layout` أو Presenter خاص بالواجهة، وليس داخل service.
- يجب الحفاظ على AutomationProperties وkeyboard navigation وreduced-motion behavior عند نقل XAML.
- عند نقل XAML يجب تحديث `x:Class` والـ namespace ومواضع `ResourceDictionary` فقط، دون تغيير السلوك في نفس الخطوة.

## 8. قواعد Dependency Injection

يبقى `App.xaml.cs` هو Composition Root الوحيد.

التسجيل المقترح:

```text
App.xaml.cs
    └── Bootstrap/ServiceCollectionExtensions.cs
        ├── Configuration options
        ├── Shared contracts
        ├── Infrastructure implementations
        ├── Feature application services
        ├── Feature ViewModels
        └── MainWindow / Views
```

القواعد:

- لا تنشئ service يدويًا داخل ViewModel.
- لا تستخدم Service Locator داخل الصفحات.
- لا تسجل نفس interface مع تنفيذين مختلفين إلا إذا كان ذلك مقصودًا ومختبرًا.
- الخدمات ذات الحالة المشتركة تسجل Singleton فقط بعد التأكد من thread-safety والـ lifecycle.
- الـ Views وViewModels الخاصة بالصفحات تسجل Transient افتراضيًا، ما لم يوجد سبب موثق.
- fallback constructors الخاصة بـ XAML designer يجب الحفاظ عليها خلال النقل، ثم لا تُزال إلا بعد التحقق من أن tooling لا يحتاجها.
- أي تغيير في DI يجب أن يرافقه اختبار resolution من `Nexora.Tests`.

## 9. خريطة نقل الملفات الحالية

هذه خريطة نقل تنظيمية؛ لا تعني تغيير السلوك:

| الموقع الحالي | الموقع المستهدف |
|---|---|
| `Features/GameLoop/PubgVersionCatalog.cs` | `Features/Graphics/Domain/` |
| `Features/GameLoop/Ue4SavEditor.cs` | `Features/Graphics/Domain/` |
| `Features/GameLoop/UnrealCVarCodec.cs` | `Features/Graphics/Domain/` |
| `Features/GameLoop/ShadowSettingsStore.cs` | `Features/Graphics/Domain/` |
| `Services/GraphicsSettingsApplier.cs` | `Features/GameLoop/Infrastructure/` |
| `Services/GameLoopService.cs` | `Features/GameLoop/Application/` |
| `Services/IGameLoopConnection.cs` | `Features/GameLoop/Application/` |
| `Services/IGraphicsProfileStore.cs` | `Features/GameLoop/Application/` |
| `Services/IAdbClient.cs` | `Features/GameLoop/Application/` |
| `Services/AdbClient.cs` | `Features/GameLoop/Infrastructure/` |
| `Services/GameLoopConnector.cs` | `Features/GameLoop/Infrastructure/` |
| `Features/GameLoop/GameLoopModels.cs` | `Features/GameLoop/Domain/` |
| `Features/GameLoop/GameLoopSession.cs` | `Features/GameLoop/Domain/` |
| `Features/GameLoop/GameLoopWorkingStorage.cs` | `Features/GameLoop/Domain/` |
| `Features/GameLoop/RemotePaths.cs` | `Features/GameLoop/Domain/` |
| `Features/GameLoop/IEmulatorSettingsService.cs` | `Features/Tuning/Application/` |
| `Features/GameLoop/EmulatorSettingsService.cs` | `Features/Tuning/Application/` |
| `Features/GameLoop/EmulatorTuningCatalog.cs` | `Features/Tuning/Domain/` |
| `Features/GameLoop/EmulatorTuningModels.cs` | `Features/Tuning/Domain/` |
| `Features/Layout/*` | `Features/Network/Application` أو `Features/Network/Domain` حسب نوع الملف |
| `Features/SystemTools/Network/*` | `Features/Network/Application` أو `Features/Network/Domain` |
| `Features/GameLoop/IShortcutService.cs` | `Features/Shortcuts/Application/` |
| `Features/GameLoop/ShortcutService.cs` | `Features/Shortcuts/Application/` |
| `Services/Performance/*` | `Features/Performance/` حسب `Application`, `Domain`, `Infrastructure` |
| `Features/Security/DefenderExclusionService.cs` | `Features/Security/Infrastructure/` |
| `Services/Update*.cs` | `Features/Updates/` حسب نوع الملف |
| `Services/ITempCleanupService.cs` | `Features/Optimizer/Application/` أو contract مشترك إذا استُخدم فعليًا في أكثر من Feature |
| `Features/SystemTools/TempCleanupService.cs` | نفس Feature المالكة له بعد التحقق من الاستخدام |
| `Shared/Infrastructure/*` | `Infrastructure/GameLoop`, `Infrastructure/Registry`, أو `Infrastructure/Files` |
| `Shared/Kernel/*` | `Shared/Kernel` أو `Shared/Contracts` |
| `UI/*` | يبقى في `UI` ما لم يكن متعلقًا بصفحة واحدة فقط |
| `MainWindow.xaml` وأحداثه | يبقى shell مؤقتًا ثم تُنقل كل صفحة إلى Feature مستقلة |

إذا كان الملف مستخدمًا من أكثر من Feature، لا يتم نسخه. يجب تحديد مالكه الحقيقي أو إبقاؤه في `Shared`/`Infrastructure` إذا كان عامًا فعلًا.

## 10. خطة النقل الآمنة

يتم النقل على مراحل صغيرة قابلة للرجوع:

### المرحلة 0 — خط أساس

```powershell
dotnet restore .\Nexora.slnx
dotnet build .\Nexora.slnx --configuration Release
dotnet test .\Nexora.slnx --configuration Release --filter "Category!=LiveFunctionalVerification"
```

يتم حفظ نتيجة البناء والاختبارات قبل أي نقل.

### المرحلة 1 — نقل الملفات غير المرتبطة بـ XAML

ابدأ بـ Domain وContracts وInfrastructure. بعد كل مجموعة:

1. تحديث namespaces وusing فقط.
2. بناء Release.
3. تشغيل الاختبارات القياسية.
4. مراجعة `git diff`.

### المرحلة 2 — فصل صفحات WPF

انقل صفحة واحدة فقط في كل مرة:

1. Graphics.
2. Tuning.
3. Network.
4. Optimizer.
5. Shortcuts.
6. About.

لا يتم نقل صفحة جديدة قبل نجاح الصفحة السابقة في التشغيل والبناء والاختبارات.

### المرحلة 3 — تخفيف MainWindow

بعد نقل الصفحات، يبقى في `MainWindow`:

- إنشاء واستضافة الصفحات.
- التنقل.
- lifecycle وإغلاق التطبيق.
- الحالة العامة المشتركة فقط.

### المرحلة 4 — التحقق النهائي

```powershell
dotnet build .\Nexora.slnx --configuration Release
dotnet test .\Nexora.slnx --configuration Release --filter "Category!=LiveFunctionalVerification"
```

ثم مراجعة:

- عدم وجود duplicate implementations في DI.
- عدم وجود مسارات ثابتة.
- عدم وجود استدعاء مباشر لـ Registry أو Process من UI.
- عدم وجود `.Result` أو `.Wait()`.
- عدم تعديل `graphify-out/`.
- عدم إضافة `bin/`, `obj/`, `artifacts/` إلى Git.

## 11. بوابة قبول أي نقل

لا يعتبر النقل مكتملًا إلا إذا تحققت كل النقاط التالية:

- البناء Release ناجح بلا warnings أو errors.
- الاختبارات القياسية ناجحة.
- التطبيق يفتح وتعمل كل الصفحات.
- الاتصال بـ GameLoop ما زال يستخدم نفس `GameLoopService` singleton.
- الحواجز الأمنية الخاصة بـ iPad وRegistry وADB ما زالت فعالة.
- لا يوجد منطق تشغيل داخل XAML.
- لا توجد ملفات مكررة أو نسخ قديمة تعمل بالتوازي.
- كل ملف موجود في مكان يطابق مسؤوليته.
- diff النقل لا يحتوي تغييرات سلوكية غير مقصودة.
- التغيير قابل للرجوع بوضوح من خلال commit منفصل لكل مرحلة.

## 12. الخلاصة المعتمدة

التنظيم المعتمد هو **Feature-First مع طبقات داخلية صغيرة**:

```text
Feature
├── Presentation
├── Application
├── Domain
└── Infrastructure (عند الحاجة)
```

هذا يحافظ على سهولة العثور على ملفات الميزة، ويمنع تداخل الواجهة مع النظام، ويجعل النقل تدريجيًا وآمنًا، بدون إنشاء ثلاثة مجلدات عامة متداخلة مثل `interface` و`Operations` و`Linking operations to the interface`.

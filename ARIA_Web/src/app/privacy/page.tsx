import Link from 'next/link';
import {
  ArrowLeft,
  ShieldCheck,
  Database,
  Activity,
  Cpu,
  Share2,
  Lock,
  UserCheck,
  FileCheck2,
  Bell,
  Mail,
} from 'lucide-react';

export const metadata = {
  title: 'Privacy Policy & Terms of Use | ARIA',
  description: 'ARIA Privacy Policy and End-User License Agreement (EULA)',
};

const sections = [
  {
    id: 'introduction',
    icon: ShieldCheck,
    title: '1. Introduction',
    body: (
      <>
        <p>
          ARIA (&ldquo;the System&rdquo;) is a reinforcement-learning decision-support tool for
          terrain-adaptive drone reforestation in Rwanda. It comprises a trained PPO policy, a Unity
          WebGL simulation, and a telemetry dashboard.
        </p>
        <p>
          <strong className="text-emerald-400">ARIA does not control physical drones and does not make
          final planting decisions.</strong> Every output is a simulated recommendation requiring field
          verification by the Rwanda Forestry Authority.
        </p>
      </>
    ),
    why: 'This framing is the ethical foundation of the whole policy: it draws a hard line between a simulation making a recommendation and a real drone acting on it, so no reader mistakes ARIA for an autonomous field system.',
  },
  {
    id: 'data-we-collect',
    icon: Database,
    title: '2. Data We Collect',
    body: (
      <>
        <p>
          ARIA collects only simulation and telemetry data: completed-episode records (zone, seeds
          placed, rewards, spacing metrics, protected-area checks) and failure/incident data generated
          during simulated missions.
        </p>
        <p>
          It does <strong>not</strong> collect names, contact details, location data about individual
          users, or any other personally identifiable information. All six geospatial datasets used to
          train the model (elevation, soil, rainfall, land cover, protected areas, species suitability)
          are openly licensed and contain no private or community-rights data.
        </p>
      </>
    ),
    why: "Matters practically because it scopes exactly what's at stake if this data were ever exposed (simulation metrics, not people), and matters ethically because it documents, in the same breath, that no community or personal data was ever incorporated into training in the first place.",
  },
  {
    id: 'how-we-use-data',
    icon: Activity,
    title: '3. How We Use Data',
    body: (
      <p>
        Episode telemetry is used to populate the real-time dashboard, to support evaluation of the
        trained policy&rsquo;s performance, and to inform future model improvement. No data collected by
        the System is used for any purpose beyond research, evaluation, and demonstration of the
        decision-support tool.
      </p>
    ),
    why: 'Purpose limitation is a core data-protection principle: users and reviewers should know data collected for one reason (evaluating the drone) is never quietly repurposed for something else.',
  },
  {
    id: 'ai-decision-making',
    icon: Cpu,
    title: '4. AI-Assisted Decision-Making',
    body: (
      <>
        <p>
          ARIA&rsquo;s unified planner-and-navigator policy is a PPO-trained reinforcement-learning model
          that recommends seed placement and reseeding actions within the simulation. It does not make
          final real-world planting decisions.
        </p>
        <p>
          All protected-area boundaries are enforced as a hard constraint, and low-confidence predictions
          are flagged rather than presented with equal certainty to higher-confidence ones. Every
          recommendation requires field verification by the Rwanda Forestry Authority before any
          real-world action is taken.
        </p>
      </>
    ),
    why: "This is the clause that most directly prevents real-world harm: it's the difference between a misjudged soil or slope reading staying a software bug versus becoming wasted seeds or ecological disruption in a sensitive area.",
  },
  {
    id: 'data-sharing',
    icon: Share2,
    title: '5. Data Sharing',
    body: (
      <p>
        Episode and telemetry data are stored in the project&rsquo;s Postgres database and are accessible
        to the project team and, where relevant, the Rwanda Forestry Authority for evaluation purposes.
        Data is not sold or shared with third parties for commercial purposes.
      </p>
    ),
    why: "Tells anyone reading exactly who can see this data and rules out the commercial resale scenario that's usually the biggest trust concern in a privacy policy, even though ARIA's data is non-personal.",
  },
  {
    id: 'data-storage-security',
    icon: Lock,
    title: '6. Data Storage & Security',
    body: (
      <ul className="list-disc list-outside pl-5 space-y-2 marker:text-emerald-500">
        <li>Telemetry data is written to Postgres via a Next.js API route at the end of each simulated episode.</li>
        <li>The dashboard reads the same data back out from Postgres when a user opens it.</li>
        <li>The System does not store any personal or community-rights data.</li>
      </ul>
    ),
    why: 'Documenting the actual write/read path (Unity → API route → Postgres → dashboard) makes this a verifiable engineering claim, not just a legal boilerplate promise — anyone can check the code against it.',
  },
  {
    id: 'your-rights',
    icon: UserCheck,
    title: '7. Your Rights',
    body: (
      <p>
        Because ARIA does not collect personal data from individual users, no personal-data access,
        correction, or deletion requests apply to the current system. Any future version that begins
        collecting personal data, for example RFA operator accounts, would need to extend this section
        accordingly.
      </p>
    ),
    why: "Honest about a real limitation rather than including boilerplate rights language that wouldn't actually mean anything yet — and flags exactly when this section would need to be rewritten (the moment real user accounts exist).",
  },
  {
    id: 'eula',
    icon: FileCheck2,
    title: '8. Terms of Use (EULA)',
    body: (
      <>
        <p>By using the ARIA dashboard or simulation, users agree to:</p>
        <ul className="list-disc list-outside pl-5 space-y-2 marker:text-emerald-500">
          <li>Treat all output as decision-support only, not as a final planting authorisation</li>
          <li>Seek field verification from the Rwanda Forestry Authority before acting on any recommendation</li>
          <li>Not misrepresent simulation results as validated field outcomes</li>
        </ul>
      </>
    ),
    why: "This is the EULA's operative clause: it binds every user, developer, and researcher to the same rule the rest of the policy is built on, so the decision-support framing can't be quietly dropped by whoever uses the system next.",
  },
  {
    id: 'changes',
    icon: Bell,
    title: '9. Changes to This Policy',
    body: (
      <p>
        This policy will be updated as ARIA moves from a research prototype toward a field-facing tool,
        particularly if future versions begin collecting operator or community data.
      </p>
    ),
    why: "Signals this is a living document tied to the system's actual maturity, not a one-time legal formality — the policy is expected to grow in step with what ARIA is allowed to do.",
  },
  {
    id: 'contact',
    icon: Mail,
    title: '10. Contact',
    body: (
      <p>
        For questions about this policy or the System, please contact the project supervisor or submit an
        issue via the project repository.
      </p>
    ),
    why: 'A real accountability channel — a policy nobody can actually reach anyone about is not meaningfully enforceable.',
  },
];

export default function PrivacyPage() {
  return (
    <div className="min-h-screen bg-slate-950 text-slate-50">
      <header className="sticky top-0 z-50 flex items-center justify-between px-4 sm:px-8 py-3 border-b border-slate-800 bg-slate-950/95 backdrop-blur-md">
        <Link href="/" className="inline-flex items-center text-sm text-slate-300 hover:text-white transition-colors">
          <ArrowLeft className="w-4 h-4 mr-1" /> Back to Home
        </Link>
        <h1 className="text-sm font-medium text-slate-300 hidden sm:block">Privacy Policy &amp; Terms of Use</h1>
        <Link
          href="/dashboard"
          className="px-4 py-1.5 rounded-full bg-white/10 hover:bg-white/20 text-white text-sm transition-all"
        >
          Dashboard
        </Link>
      </header>

      <main className="max-w-6xl mx-auto px-4 sm:px-8 py-12">
        {/* Title block */}
        <div className="mb-12 max-w-3xl">
          <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-emerald-500/10 border border-emerald-500/20 text-emerald-400 text-xs font-medium mb-6">
            <ShieldCheck className="w-3.5 h-3.5" />
            Last updated: July 2026
          </div>
          <h1 className="text-4xl md:text-5xl font-bold tracking-tight mb-4">
            Privacy Policy &amp;{' '}
            <span className="text-transparent bg-clip-text bg-gradient-to-r from-emerald-400 to-cyan-400">
              Terms of Use
            </span>
          </h1>
          <p className="text-lg text-slate-400 leading-relaxed">
            How ARIA handles data, what its AI-generated recommendations do and don&rsquo;t authorise, and
            the terms every user, developer, and researcher agrees to by using the System.
          </p>
        </div>

        <div className="lg:grid lg:grid-cols-[240px_1fr] lg:gap-12">
          {/* Sticky table of contents */}
          <nav className="hidden lg:block">
            <div className="sticky top-24 space-y-1">
              <p className="text-xs font-semibold uppercase tracking-wider text-slate-500 mb-3 px-3">
                On this page
              </p>
              {sections.map((s) => (
                <a
                  key={s.id}
                  href={`#${s.id}`}
                  className="flex items-center gap-2.5 px-3 py-2 rounded-lg text-sm text-slate-400 hover:text-white hover:bg-white/5 transition-colors"
                >
                  <s.icon className="w-4 h-4 shrink-0 text-slate-500" />
                  <span className="truncate">{s.title.replace(/^\d+\.\s*/, '')}</span>
                </a>
              ))}
            </div>
          </nav>

          {/* Mobile quick-nav */}
          <nav className="lg:hidden mb-10 -mx-4 px-4 overflow-x-auto">
            <div className="flex gap-2 w-max pb-2">
              {sections.map((s) => (
                <a
                  key={s.id}
                  href={`#${s.id}`}
                  className="flex items-center gap-1.5 px-3 py-1.5 rounded-full bg-slate-900 border border-slate-800 text-xs text-slate-300 whitespace-nowrap hover:bg-slate-800 transition-colors"
                >
                  <s.icon className="w-3.5 h-3.5" />
                  {s.title.replace(/^\d+\.\s*/, '')}
                </a>
              ))}
            </div>
          </nav>

          {/* Sections */}
          <div className="space-y-6 min-w-0">
            {sections.map((s) => (
              <section
                key={s.id}
                id={s.id}
                className="scroll-mt-24 p-6 sm:p-8 rounded-3xl bg-slate-900/50 border border-slate-800 backdrop-blur-sm"
              >
                <div className="flex items-start gap-4 mb-4">
                  <div className="w-11 h-11 shrink-0 rounded-2xl bg-emerald-500/10 flex items-center justify-center text-emerald-400">
                    <s.icon className="w-5 h-5" />
                  </div>
                  <h2 className="text-xl sm:text-2xl font-semibold text-slate-100 pt-2">{s.title}</h2>
                </div>

                <div className="text-slate-300 leading-relaxed space-y-3 pl-0 sm:pl-[3.75rem]">
                  {s.body}
                </div>

                <div className="mt-5 ml-0 sm:ml-[3.75rem] flex gap-3 rounded-2xl bg-cyan-500/5 border border-cyan-500/15 p-4">
                  <span className="text-xs font-semibold uppercase tracking-wider text-cyan-400 shrink-0 pt-0.5">
                    Why it matters
                  </span>
                  <p className="text-sm text-slate-400 leading-relaxed">{s.why}</p>
                </div>
              </section>
            ))}
          </div>
        </div>

        <p className="text-center text-sm text-slate-600 mt-16">
          &copy; {new Date().getFullYear()} ARIA. Adaptive Reforestation Intelligence Agent.
        </p>
      </main>
    </div>
  );
}

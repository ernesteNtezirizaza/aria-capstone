'use client';

import { useState } from 'react';
import { UserPlus, Trash2, RefreshCw, X } from 'lucide-react';

type Role = 'ADMIN' | 'FORESTER';
type Status = 'PENDING' | 'ACTIVE' | 'DISABLED';

type AdminUser = {
  id: number;
  name: string;
  email: string;
  role: Role;
  status: Status;
  createdAt: string | Date;
  createdBy: { name: string } | null;
  _count: { episodes: number };
};

const STATUS_STYLES: Record<Status, string> = {
  PENDING: 'bg-amber-50 dark:bg-amber-500/10 text-amber-600 dark:text-amber-400 border-amber-200 dark:border-amber-500/20',
  ACTIVE: 'bg-emerald-50 dark:bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 border-emerald-200 dark:border-emerald-500/20',
  DISABLED: 'bg-red-50 dark:bg-red-500/10 text-red-600 dark:text-red-400 border-red-200 dark:border-red-500/20',
};

const ROLE_STYLES: Record<Role, string> = {
  ADMIN: 'bg-cyan-50 dark:bg-cyan-500/10 text-cyan-600 dark:text-cyan-400 border-cyan-200 dark:border-cyan-500/20',
  FORESTER: 'bg-indigo-50 dark:bg-indigo-500/10 text-indigo-600 dark:text-indigo-400 border-indigo-200 dark:border-indigo-500/20',
};

export default function AdminUsersClient({
  initialUsers,
  currentUserId,
}: {
  initialUsers: AdminUser[];
  currentUserId: number;
}) {
  const [users, setUsers] = useState<AdminUser[]>(initialUsers);
  const [showCreate, setShowCreate] = useState(false);
  const [rowError, setRowError] = useState<Record<number, string>>({});
  const [busyRow, setBusyRow] = useState<number | null>(null);

  async function refetch() {
    const res = await fetch('/api/admin/users');
    if (res.ok) {
      const data = await res.json();
      setUsers(data.users);
    }
  }

  async function handleRoleChange(user: AdminUser, role: Role) {
    setBusyRow(user.id);
    setRowError((e) => ({ ...e, [user.id]: '' }));
    try {
      const res = await fetch(`/api/admin/users/${user.id}`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ role }),
      });
      const data = await res.json();
      if (!res.ok) throw new Error(data.error || 'Could not update role.');
      await refetch();
    } catch (err) {
      setRowError((e) => ({ ...e, [user.id]: err instanceof Error ? err.message : 'Could not update role.' }));
    } finally {
      setBusyRow(null);
    }
  }

  async function handleStatusToggle(user: AdminUser) {
    const nextStatus: Status = user.status === 'DISABLED' ? 'ACTIVE' : 'DISABLED';
    setBusyRow(user.id);
    setRowError((e) => ({ ...e, [user.id]: '' }));
    try {
      const res = await fetch(`/api/admin/users/${user.id}`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ status: nextStatus }),
      });
      const data = await res.json();
      if (!res.ok) throw new Error(data.error || 'Could not update status.');
      await refetch();
    } catch (err) {
      setRowError((e) => ({ ...e, [user.id]: err instanceof Error ? err.message : 'Could not update status.' }));
    } finally {
      setBusyRow(null);
    }
  }

  async function handleResendInvite(user: AdminUser) {
    setBusyRow(user.id);
    setRowError((e) => ({ ...e, [user.id]: '' }));
    try {
      const res = await fetch('/api/auth/resend-otp', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email: user.email, purpose: 'VERIFY_EMAIL' }),
      });
      const data = await res.json();
      if (!res.ok) throw new Error(data.error || 'Could not resend invitation.');
    } catch (err) {
      setRowError((e) => ({ ...e, [user.id]: err instanceof Error ? err.message : 'Could not resend invitation.' }));
    } finally {
      setBusyRow(null);
    }
  }

  async function handleDelete(user: AdminUser) {
    if (!window.confirm(`Delete ${user.name} (${user.email})? This cannot be undone.`)) return;
    setBusyRow(user.id);
    setRowError((e) => ({ ...e, [user.id]: '' }));
    try {
      const res = await fetch(`/api/admin/users/${user.id}`, { method: 'DELETE' });
      const data = await res.json();
      if (!res.ok) throw new Error(data.error || 'Could not delete user.');
      setUsers((prev) => prev.filter((u) => u.id !== user.id));
    } catch (err) {
      setRowError((e) => ({ ...e, [user.id]: err instanceof Error ? err.message : 'Could not delete user.' }));
    } finally {
      setBusyRow(null);
    }
  }

  return (
    <div>
      <div className="flex justify-end mb-6">
        <button
          onClick={() => setShowCreate(true)}
          className="flex items-center gap-2 px-5 py-2.5 rounded-full bg-emerald-600 hover:bg-emerald-500 text-white font-medium transition-colors"
        >
          <UserPlus className="w-4 h-4" />
          New User
        </button>
      </div>

      <div className="rounded-2xl bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 shadow-sm overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-sm text-left whitespace-nowrap">
            <thead className="text-xs text-slate-500 uppercase bg-slate-50 dark:bg-slate-800/50">
              <tr>
                <th className="px-4 sm:px-6 py-4 font-medium">Name</th>
                <th className="px-4 sm:px-6 py-4 font-medium">Email</th>
                <th className="px-4 sm:px-6 py-4 font-medium">Role</th>
                <th className="px-4 sm:px-6 py-4 font-medium">Status</th>
                <th className="px-4 sm:px-6 py-4 font-medium">Episodes</th>
                <th className="px-4 sm:px-6 py-4 font-medium">Created</th>
                <th className="px-4 sm:px-6 py-4 font-medium text-right">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-200 dark:divide-slate-800">
              {users.map((user) => {
                const isSelf = user.id === currentUserId;
                const busy = busyRow === user.id;
                return (
                  <tr key={user.id} className="hover:bg-slate-50 dark:hover:bg-slate-800/30 transition-colors align-top">
                    <td className="px-4 sm:px-6 py-4 font-medium">
                      {user.name}
                      {isSelf && <span className="ml-2 text-xs text-slate-500">(you)</span>}
                    </td>
                    <td className="px-4 sm:px-6 py-4 text-slate-500 dark:text-slate-300">{user.email}</td>
                    <td className="px-4 sm:px-6 py-4">
                      <select
                        value={user.role}
                        disabled={busy || isSelf}
                        onChange={(e) => handleRoleChange(user, e.target.value as Role)}
                        className={`text-xs font-medium rounded-full border px-2.5 py-1 bg-transparent disabled:opacity-50 ${ROLE_STYLES[user.role]}`}
                      >
                        <option className="bg-white dark:bg-slate-900" value="ADMIN">ADMIN</option>
                        <option className="bg-white dark:bg-slate-900" value="FORESTER">FORESTER</option>
                      </select>
                    </td>
                    <td className="px-4 sm:px-6 py-4">
                      <span className={`text-xs font-medium rounded-full border px-2.5 py-1 ${STATUS_STYLES[user.status]}`}>
                        {user.status}
                      </span>
                    </td>
                    <td className="px-4 sm:px-6 py-4 font-mono text-slate-400">{user._count.episodes}</td>
                    <td className="px-4 sm:px-6 py-4 text-slate-500 text-xs">
                      {new Date(user.createdAt).toLocaleDateString()}
                      {user.createdBy && <div>by {user.createdBy.name}</div>}
                    </td>
                    <td className="px-4 sm:px-6 py-4">
                      <div className="flex items-center justify-end gap-2">
                        {user.status === 'PENDING' && (
                          <button
                            onClick={() => handleResendInvite(user)}
                            disabled={busy}
                            title="Resend invitation email"
                            className="p-2 rounded-full hover:bg-slate-100 dark:hover:bg-white/10 text-slate-400 hover:text-slate-900 dark:hover:text-white disabled:opacity-50 transition-colors"
                          >
                            <RefreshCw className="w-4 h-4" />
                          </button>
                        )}
                        {user.status !== 'PENDING' && (
                          <button
                            onClick={() => handleStatusToggle(user)}
                            disabled={busy || isSelf}
                            className="text-xs px-3 py-1.5 rounded-full border border-slate-200 dark:border-slate-700 hover:bg-slate-100 dark:hover:bg-white/10 text-slate-600 dark:text-slate-300 disabled:opacity-50 transition-colors"
                          >
                            {user.status === 'DISABLED' ? 'Activate' : 'Disable'}
                          </button>
                        )}
                        <button
                          onClick={() => handleDelete(user)}
                          disabled={busy || isSelf}
                          title="Delete user"
                          className="p-2 rounded-full hover:bg-red-50 dark:hover:bg-red-500/10 text-slate-400 hover:text-red-500 dark:hover:text-red-400 disabled:opacity-50 transition-colors"
                        >
                          <Trash2 className="w-4 h-4" />
                        </button>
                      </div>
                      {rowError[user.id] && (
                        <p className="text-xs text-red-500 dark:text-red-400 mt-1.5 text-right">{rowError[user.id]}</p>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
          {users.length === 0 && (
            <div className="p-12 text-center text-slate-500">No users yet.</div>
          )}
        </div>
      </div>

      {showCreate && (
        <CreateUserModal
          onClose={() => setShowCreate(false)}
          onCreated={() => {
            setShowCreate(false);
            refetch();
          }}
        />
      )}
    </div>
  );
}

function CreateUserModal({ onClose, onCreated }: { onClose: () => void; onCreated: () => void }) {
  const [name, setName] = useState('');
  const [email, setEmail] = useState('');
  const [role, setRole] = useState<Role>('FORESTER');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setLoading(true);
    try {
      const res = await fetch('/api/admin/users', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name, email, role }),
      });
      const data = await res.json();
      if (!res.ok && res.status !== 207) throw new Error(data.error || 'Could not create user.');
      onCreated();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not create user.');
    } finally {
      setLoading(false);
    }
  }

  const inputClass =
    'w-full rounded-xl bg-white/5 border border-slate-700 px-4 py-2.5 text-sm text-white placeholder:text-slate-500 focus:outline-none focus:border-emerald-500/50';

  return (
    <div className="fixed inset-0 z-[200] flex items-center justify-center bg-black/60 backdrop-blur-sm px-4">
      <div className="w-full max-w-md rounded-3xl bg-slate-900 border border-slate-800 p-6 sm:p-8">
        <div className="flex items-center justify-between mb-6">
          <h2 className="text-lg font-semibold text-white">Create new user</h2>
          <button onClick={onClose} className="p-1.5 rounded-full hover:bg-white/10 text-slate-400 hover:text-white transition-colors">
            <X className="w-4 h-4" />
          </button>
        </div>
        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="block text-xs font-medium text-slate-400 mb-1.5">Full name</label>
            <input required value={name} onChange={(e) => setName(e.target.value)} className={inputClass} placeholder="Jane Doe" />
          </div>
          <div>
            <label className="block text-xs font-medium text-slate-400 mb-1.5">Email</label>
            <input required type="email" value={email} onChange={(e) => setEmail(e.target.value)} className={inputClass} placeholder="jane@example.com" />
          </div>
          <div>
            <label className="block text-xs font-medium text-slate-400 mb-1.5">Role</label>
            <select value={role} onChange={(e) => setRole(e.target.value as Role)} className={inputClass}>
              <option value="FORESTER">Forester</option>
              <option value="ADMIN">Admin</option>
            </select>
          </div>

          {error && (
            <p className="text-xs text-red-400 bg-red-500/10 border border-red-500/20 rounded-xl px-3 py-2">{error}</p>
          )}

          <p className="text-xs text-slate-500">
            The new user will receive a verification code by email to activate their account and set their password.
          </p>

          <button
            type="submit"
            disabled={loading}
            className="w-full rounded-full bg-emerald-600 hover:bg-emerald-500 disabled:opacity-50 text-white font-medium py-2.5 transition-colors"
          >
            {loading ? 'Creating...' : 'Create user & send invite'}
          </button>
        </form>
      </div>
    </div>
  );
}

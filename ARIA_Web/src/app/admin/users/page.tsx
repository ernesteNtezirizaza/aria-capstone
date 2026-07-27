import { redirect } from 'next/navigation';
import { getSession } from '@/lib/auth';
import prisma from '@/lib/prisma';
import AdminUsersClient from './AdminUsersClient';

export const dynamic = 'force-dynamic';

export default async function AdminUsersPage() {
  const session = await getSession();
  if (!session || session.role !== 'ADMIN') redirect('/dashboard');

  const users = await prisma.user.findMany({
    orderBy: { createdAt: 'desc' },
    select: {
      id: true,
      name: true,
      email: true,
      role: true,
      status: true,
      createdAt: true,
      createdBy: { select: { name: true } },
      _count: { select: { episodes: true } },
    },
  });

  return (
    <div className="min-h-screen bg-slate-950 text-slate-50">
      <AdminUsersClient initialUsers={users} currentUserId={Number(session.sub)} />
    </div>
  );
}

import { NextResponse } from 'next/server';
import { z } from 'zod';
import prisma from '@/lib/prisma';
import { getSession } from '@/lib/auth';

const patchSchema = z.object({
  name: z.string().min(1, 'Name is required.').optional(),
  role: z.enum(['ADMIN', 'FORESTER']).optional(),
  status: z.enum(['ACTIVE', 'DISABLED']).optional(),
});

async function countOtherActiveAdmins(excludeUserId: number): Promise<number> {
  return prisma.user.count({
    where: { role: 'ADMIN', status: 'ACTIVE', id: { not: excludeUserId } },
  });
}

export async function PATCH(req: Request, { params }: { params: Promise<{ id: string }> }) {
  const session = await getSession();
  if (!session || session.role !== 'ADMIN') {
    return NextResponse.json({ error: 'Forbidden.' }, { status: 403 });
  }

  const { id } = await params;
  const targetId = Number(id);
  if (!Number.isInteger(targetId)) {
    return NextResponse.json({ error: 'Invalid user id.' }, { status: 400 });
  }

  const parsed = patchSchema.safeParse(await req.json().catch(() => null));
  if (!parsed.success) {
    return NextResponse.json({ error: 'Invalid request.' }, { status: 400 });
  }
  const { name, role, status } = parsed.data;

  const target = await prisma.user.findUnique({ where: { id: targetId } });
  if (!target) {
    return NextResponse.json({ error: 'User not found.' }, { status: 404 });
  }

  const isSelf = targetId === Number(session.sub);
  const demotingSelf = isSelf && role === 'FORESTER' && target.role === 'ADMIN';
  const disablingSelf = isSelf && status === 'DISABLED';
  if (demotingSelf || disablingSelf) {
    return NextResponse.json({ error: "You can't remove your own admin access." }, { status: 400 });
  }

  /* Prevent the system from ending up with zero active admins. */
  const wouldLoseAdminRole = target.role === 'ADMIN' && role === 'FORESTER';
  const wouldBeDisabled = target.status === 'ACTIVE' && status === 'DISABLED';
  if ((wouldLoseAdminRole || wouldBeDisabled) && target.role === 'ADMIN' && target.status === 'ACTIVE') {
    const remaining = await countOtherActiveAdmins(targetId);
    if (remaining === 0) {
      return NextResponse.json({ error: 'At least one active admin must remain.' }, { status: 400 });
    }
  }

  const updated = await prisma.user.update({
    where: { id: targetId },
    data: { ...(name ? { name } : {}), ...(role ? { role } : {}), ...(status ? { status } : {}) },
    select: { id: true, name: true, email: true, role: true, status: true },
  });

  return NextResponse.json({ success: true, user: updated });
}

export async function DELETE(_req: Request, { params }: { params: Promise<{ id: string }> }) {
  const session = await getSession();
  if (!session || session.role !== 'ADMIN') {
    return NextResponse.json({ error: 'Forbidden.' }, { status: 403 });
  }

  const { id } = await params;
  const targetId = Number(id);
  if (!Number.isInteger(targetId)) {
    return NextResponse.json({ error: 'Invalid user id.' }, { status: 400 });
  }

  if (targetId === Number(session.sub)) {
    return NextResponse.json({ error: "You can't delete your own account." }, { status: 400 });
  }

  const target = await prisma.user.findUnique({ where: { id: targetId } });
  if (!target) {
    return NextResponse.json({ error: 'User not found.' }, { status: 404 });
  }

  if (target.role === 'ADMIN' && target.status === 'ACTIVE') {
    const remaining = await countOtherActiveAdmins(targetId);
    if (remaining === 0) {
      return NextResponse.json({ error: 'At least one active admin must remain.' }, { status: 400 });
    }
  }

  /* Episodes created by this user are kept for historical dashboard stats;
     only the FK is cleared rather than cascading the delete. */
  await prisma.episode.updateMany({ where: { user_id: targetId }, data: { user_id: null } });
  await prisma.user.updateMany({ where: { createdById: targetId }, data: { createdById: null } });
  await prisma.user.delete({ where: { id: targetId } });

  return NextResponse.json({ success: true });
}

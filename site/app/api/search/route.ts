import { createFromSource } from 'fumadocs-core/search/server';
import { getSource } from '@/lib/source';

const search = createFromSource(getSource);

export const GET = search.GET;

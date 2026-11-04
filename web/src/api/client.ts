import createClient from 'openapi-fetch';
import type { paths, components } from './schema';

export const api = createClient<paths>({
  baseUrl: '',
  credentials: 'include',
  headers: {
    'Accept': 'application/json',
  },
});

export type Schemas = components['schemas'];
export type PriceMatrixResponse = Schemas['PriceMatrixResponseDto'];
export type PriceMatrixRow = Schemas['PriceMatrixRowDto'];
export type CompetitorPriceCell = Schemas['CompetitorPriceCellDto'];
export type CompetitorOffersResponse = Schemas['CompetitorOffersResponseDto'];
export type CompetitorOffer = Schemas['CompetitorOfferDto'];
export type ReferenceOffersResponse = Schemas['ReferenceOffersResponseDto'];
export type ReferenceOffer = Schemas['ReferenceOfferDto'];
export type AuthenticatedUser = Schemas['AuthenticatedUserDto'];
export type CollectionRun = Schemas['CollectionRunSummaryDto'];
export type Competitor = Schemas['CompetitorDto'];

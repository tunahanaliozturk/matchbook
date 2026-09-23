-- One database per service, each owned by its own login role and closed to every other role, so a service
-- cannot read another's tables even by mistake. In production these would be separate servers; one container
-- keeps the demo small without weakening the boundary.
--
-- The reconciler role reads every database. It is used only by the system tests, to check the invariants that
-- span services, and holds no write privilege anywhere.

create role reconciler login password 'reconciler';

create role suppliers login password 'suppliers';
create database suppliers owner suppliers;
revoke all on database suppliers from public;
grant connect on database suppliers to reconciler;

create role budgets login password 'budgets';
create database budgets owner budgets;
revoke all on database budgets from public;
grant connect on database budgets to reconciler;

create role requisitions login password 'requisitions';
create database requisitions owner requisitions;
revoke all on database requisitions from public;
grant connect on database requisitions to reconciler;

create role purchasing login password 'purchasing';
create database purchasing owner purchasing;
revoke all on database purchasing from public;
grant connect on database purchasing to reconciler;

create role payables login password 'payables';
create database payables owner payables;
revoke all on database payables from public;
grant connect on database payables to reconciler;

\connect suppliers
alter default privileges for role suppliers in schema public grant select on tables to reconciler;
grant usage on schema public to reconciler;

\connect budgets
alter default privileges for role budgets in schema public grant select on tables to reconciler;
grant usage on schema public to reconciler;

\connect requisitions
alter default privileges for role requisitions in schema public grant select on tables to reconciler;
grant usage on schema public to reconciler;

\connect purchasing
alter default privileges for role purchasing in schema public grant select on tables to reconciler;
grant usage on schema public to reconciler;

\connect payables
alter default privileges for role payables in schema public grant select on tables to reconciler;
grant usage on schema public to reconciler;

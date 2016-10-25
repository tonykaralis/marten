﻿using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Linq.QueryHandlers;
using Marten.Schema;
using Marten.Services.Includes;
using Marten.Util;
using Npgsql;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.ResultOperators;

namespace Marten.Linq.Model
{
    public class LinqQuery<T> : ListQueryHandler<T>
    {
        private readonly QueryModel _query;
        private readonly IDocumentSchema _schema;
        private readonly IQueryableDocument _mapping;

        private readonly SelectManyQuery _subQuery;

        public LinqQuery(IDocumentSchema schema, QueryModel query, IIncludeJoin[] joins, QueryStatistics stats)
            : base(BuildSelector(schema, query, joins, stats))
        {
            _query = query;
            _schema = schema;
            _mapping = schema.MappingFor(query).ToQueryableDocument();

            for (int i = 0; i < query.BodyClauses.Count; i++)
            {
                var clause = query.BodyClauses[i];
                if (clause is AdditionalFromClause)
                {
                    // TODO -- to be able to go recursive, have _subQuery start to read the BodyClauses
                    _subQuery = new SelectManyQuery(_mapping, query, i + 1);


                    break;
                }
            }

            SourceType = _query.SourceType();

            Where = buildWhereFragment();
        }

        public IWhereFragment Where { get; set; }

        public override Type SourceType { get; }

        public static ISelector<T> BuildSelector(IDocumentSchema schema, QueryModel query,
            IIncludeJoin[] joins, QueryStatistics stats)
        {
            var mapping = schema.MappingFor(query).ToQueryableDocument();
            var selector = schema.BuildSelector<T>(mapping, query);

            if (stats != null)
            {
                selector = new StatsSelector<T>(stats, selector);
            }

            if (joins.Any())
            {
                selector = new IncludeSelector<T>(schema, selector, joins);
            }

            return selector;
        }

        public override void ConfigureCommand(NpgsqlCommand command)
        {
            ConfigureCommand(command, 0);
        }

        public void ConfigureCommand(NpgsqlCommand command, int limit)
        {
            var sql = Selector.ToSelectClause(_mapping);

            string filter = null;
            if (Where != null)
            {
                filter = Where.ToSql(command);
            }

            if (filter.IsNotEmpty())
            {
                sql += " where " + filter;
            }

            var orderBy = determineOrderClause();

            if (orderBy.IsNotEmpty()) sql += orderBy;

            if (limit > 0)
            {
                sql += " LIMIT " + limit;
            }
            else
            {
                var take = _query.FindOperators<TakeResultOperator>().LastOrDefault();
                if (take != null)
                {
                    var param = command.AddParameter(take.Count.Value());
                    sql += " LIMIT :" + param.ParameterName;
                }
            }

            var skip = _query.FindOperators<SkipResultOperator>().LastOrDefault();
            if (skip != null)
            {
                var param = command.AddParameter(skip.Count.Value());
                sql += " OFFSET :" + param.ParameterName;
            }

            command.AppendQuery(sql);
        }

        private string determineOrderClause()
        {
            var orders = bodyClauses().OfType<OrderByClause>().SelectMany(x => x.Orderings).ToArray();
            if (!orders.Any()) return string.Empty;

            return " order by " + orders.Select(toOrderClause).Join(", ");
        }

        private string toOrderClause(Ordering clause)
        {
            var locator = _mapping.JsonLocator(clause.Expression);
            return clause.OrderingDirection == OrderingDirection.Asc
                ? locator
                : locator + " desc";
        }

        private IWhereFragment buildWhereFragment()
        {
            var bodies = bodyClauses();

            var wheres = bodies.OfType<WhereClause>().ToArray();
            if (wheres.Length == 0) return _mapping.DefaultWhereFragment();

            var where = wheres.Length == 1
                ? _schema.Parser.ParseWhereFragment(_mapping, wheres.Single().Predicate)
                : new CompoundWhereFragment(_schema.Parser, _mapping, "and", wheres);

            return _mapping.FilterDocuments(_query, where);
        }

        private IEnumerable<IBodyClause> bodyClauses()
        {
            var bodies = _subQuery == null
                ? _query.AllBodyClauses()
                : _query.BodyClauses.Take(_subQuery.Index);

            return bodies;
        }
    }
}
namespace ThriveDevCenter.Server.Tests.Models.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using AngleSharp.Html.Parser;
using Server.Models;
using Shared.Models;
using Xunit;

public class FeedTests
{
    private const string TestGithubFeedContent = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<feed xmlns=""http://www.w3.org/2005/Atom"" xmlns:media=""http://search.yahoo.com/mrss/"" xml:lang=""en-US"">
  <id>tag:github.com,2008:/organizations/Revolutionary-Games/user</id>
  <link type=""text/html"" rel=""alternate"" href=""https://github.com/organizations/Revolutionary-Games/user""/>
  <link type=""application/atom+xml"" rel=""self"" href=""https://github.com/organizations/Revolutionary-Games/user.private.atom?token=abcd""/>
  <title>Private Feed for the Revolutionary-Games Organization</title>
  <updated>2022-05-29T13:39:32Z</updated>
  <entry>
    <id>tag:github.com,2008:CreateEvent/22041400336</id>
    <published>2022-05-29T12:12:03Z</published>
    <updated>2022-05-29T12:12:03Z</updated>
    <link type=""text/html"" rel=""alternate"" href=""https://github.com/Revolutionary-Games/Thrive/compare/improve_chemoreceptor_visuals""/>
    <title type=""html"">User2 created a branch improve_chemoreceptor_visuals in Revolutionary-Games/Thrive</title>
    <author>
      <name>User2</name>
      <uri>https://github.com/User2</uri>
    </author>
    <media:thumbnail height=""30"" width=""30"" url=""https://avatars.githubusercontent.com/u/7430433?s=30&amp;v=4""/>
    <content type=""html"">&lt;div class=&quot;git-branch&quot;&gt;&lt;div class=&quot;body&quot;&gt;
&lt;!-- create --&gt;
&lt;div class=&quot;d-flex flex-items-baseline border-bottom color-border-muted py-3&quot;&gt;
      &lt;span class=&quot;mr-2&quot;&gt;&lt;a class=&quot;d-inline-block&quot; href=&quot;/User2&quot; rel=&quot;noreferrer&quot;&gt;&lt;img class=&quot;avatar avatar-user&quot; src=&quot;https://avatars.githubusercontent.com/u/7430433?s=64&amp;amp;v=4&quot; width=&quot;32&quot; height=&quot;32&quot; alt=&quot;@User2&quot;&gt;&lt;/a&gt;&lt;/span&gt;
  &lt;div class=&quot;d-flex flex-column width-full&quot;&gt;
        &lt;div class=&quot;d-flex flex-items-baseline&quot;&gt;
          &lt;div class=&quot;&quot;&gt;
              &lt;a class=&quot;Link--primary no-underline wb-break-all text-bold d-inline-block&quot; href=&quot;/User2&quot; rel=&quot;noreferrer&quot;&gt;User2&lt;/a&gt;
              created a
              branch
              in
              &lt;a class=&quot;Link--primary no-underline wb-break-all text-bold d-inline-block&quot; href=&quot;/Revolutionary-Games/Thrive&quot; rel=&quot;noreferrer&quot;&gt;Revolutionary-Games/Thrive&lt;/a&gt;
              &lt;span class=&quot;f6 color-fg-muted no-wrap ml-1&quot;&gt;
                &lt;relative-time datetime=&quot;2022-05-29T12:12:03Z&quot; class=&quot;no-wrap&quot;&gt;May 29, 2022&lt;/relative-time&gt;
              &lt;/span&gt;
          &lt;/div&gt;
        &lt;/div&gt;
    &lt;div class=&quot;Box p-3 mt-2 &quot;&gt;
      &lt;div&gt;
        &lt;div class=&quot;f4 lh-condensed text-bold color-fg-default&quot;&gt;
              &lt;a class=&quot;css-truncate css-truncate-target branch-name v-align-middle&quot; title=&quot;improve_chemoreceptor_visuals&quot; href=&quot;/Revolutionary-Games/Thrive/tree/improve_chemoreceptor_visuals&quot; rel=&quot;noreferrer&quot;&gt;improve_chemoreceptor_visuals&lt;/a&gt; in &lt;a class=&quot;Link--primary text-bold no-underline wb-break-all d-inline-block&quot; href=&quot;/Revolutionary-Games/Thrive&quot; rel=&quot;noreferrer&quot;&gt;Revolutionary-Games/Thrive&lt;/a&gt;
        &lt;/div&gt;
          &lt;p class=&quot;f6 color-fg-muted mt-2 mb-0&quot;&gt;
            &lt;span&gt;Updated May 29&lt;/span&gt;
          &lt;/p&gt;
      &lt;/div&gt;
    &lt;/div&gt;
  &lt;/div&gt;
&lt;/div&gt;
&lt;/div&gt;&lt;/div&gt;</content>
  </entry>
  <entry>
    <id>tag:github.com,2008:PushEvent/22041370895</id>
    <published>2022-05-29T12:07:23Z</published>
    <updated>2022-05-29T12:07:23Z</updated>
    <link type=""text/html"" rel=""alternate"" href=""https://github.com/Revolutionary-Games/Thrive/compare/2fc3f4d82c...dd5375c068""/>
    <title type=""html"">User3 pushed to art_gallery in Revolutionary-Games/Thrive</title>
    <author>
      <name>User3</name>
      <uri>https://github.com/User3</uri>
    </author>
    <media:thumbnail height=""30"" width=""30"" url=""https://avatars.githubusercontent.com/u/54026083?s=30&amp;v=4""/>
    <content type=""html"">&lt;div class=&quot;push&quot;&gt;&lt;div class=&quot;body&quot;&gt;
&lt;!-- push --&gt;
&lt;div class=&quot;d-flex flex-items-baseline border-bottom color-border-muted py-3&quot;&gt;
    &lt;span class=&quot;mr-2&quot;&gt;&lt;a class=&quot;d-inline-block&quot; href=&quot;/User3&quot; rel=&quot;noreferrer&quot;&gt;&lt;img class=&quot;avatar avatar-user&quot; src=&quot;https://avatars.githubusercontent.com/u/54026083?s=64&amp;amp;v=4&quot; width=&quot;32&quot; height=&quot;32&quot; alt=&quot;@User3&quot;&gt;&lt;/a&gt;&lt;/span&gt;
  &lt;div class=&quot;d-flex flex-column width-full&quot;&gt;
    &lt;div class=&quot;&quot;&gt;
      &lt;a class=&quot;Link--primary no-underline wb-break-all text-bold d-inline-block&quot; href=&quot;/User3&quot; rel=&quot;noreferrer&quot;&gt;User3&lt;/a&gt;
      pushed to
      &lt;a class=&quot;Link--primary no-underline wb-break-all text-bold d-inline-block&quot; href=&quot;/Revolutionary-Games/Thrive&quot; rel=&quot;noreferrer&quot;&gt;Revolutionary-Games/Thrive&lt;/a&gt;
        &lt;span class=&quot;color-fg-muted no-wrap f6 ml-1&quot;&gt;
          &lt;relative-time datetime=&quot;2022-05-29T12:07:23Z&quot; class=&quot;no-wrap&quot;&gt;May 29, 2022&lt;/relative-time&gt;
        &lt;/span&gt;
        &lt;div class=&quot;Box p-3 mt-2 &quot;&gt;
          &lt;span&gt;1 commit to&lt;/span&gt;
          &lt;a class=&quot;branch-name&quot; href=&quot;/Revolutionary-Games/Thrive/tree/art_gallery&quot; rel=&quot;noreferrer&quot;&gt;art_gallery&lt;/a&gt;
          &lt;div class=&quot;commits &quot;&gt;
            &lt;ul class=&quot;list-style-none&quot;&gt;
                &lt;li class=&quot;d-flex flex-items-baseline&quot;&gt;
                  &lt;span title=&quot;User3&quot;&gt;
                    &lt;img class=&quot;mr-1 avatar-user&quot; width=&quot;16&quot; height=&quot;16&quot; alt=&quot;&quot; src=&quot;https://camo.githubusercontent.com/7d5293fca7ee9d8703b4c96c1a585df2cf5b32f4a61d2f85d08315e78f5101a2/68747470733a2f2f312e67726176617461722e636f6d2f6176617461722f37613137343735643538366232623030326565663463653639646437623163393f643d68747470732533412532462532466769746875622e6769746875626173736574732e636f6d253246696d6167657325324667726176617461727325324667726176617461722d757365722d3432302e706e6726723d6726733d313430&quot; data-canonical-src=&quot;https://1.gravatar.com/avatar/7a17475d586b2b002eef4ce69dd7b1c9?d=https%3A%2F%2Fgithub.githubassets.com%2Fimages%2Fgravatars%2Fgravatar-user-420.png&amp;amp;r=g&amp;amp;s=140&quot;&gt;
                  &lt;/span&gt;
                  &lt;code&gt;&lt;a class=&quot;mr-1&quot; href=&quot;/Revolutionary-Games/Thrive/commit/dd5375c0682bc469171a513c6e8317760377add3&quot; rel=&quot;noreferrer&quot;&gt;dd5375c&lt;/a&gt;&lt;/code&gt;
                  &lt;div class=&quot;dashboard-break-word lh-condensed&quot;&gt;
                    &lt;blockquote&gt;
                      Further work on the music player part of the gallery.
                    &lt;/blockquote&gt;
                  &lt;/div&gt;
                &lt;/li&gt;
            &lt;/ul&gt;
          &lt;/div&gt;
        &lt;/div&gt;
    &lt;/div&gt;
  &lt;/div&gt;
&lt;/div&gt;
&lt;/div&gt;&lt;/div&gt;</content>
  </entry>
  <entry>
    <id>tag:github.com,2008:PullRequestEvent/22037544822</id>
    <published>2022-05-28T20:50:46Z</published>
    <updated>2022-05-28T20:50:46Z</updated>
    <link type=""text/html"" rel=""alternate"" href=""https://github.com/Revolutionary-Games/Thrive/pull/3375""/>
    <title type=""html"">revolutionary-translation-bot opened a pull request in Revolutionary-Games/Thrive</title>
    <author>
      <name>revolutionary-translation-bot</name>
      <uri>https://github.com/revolutionary-translation-bot</uri>
    </author>
    <media:thumbnail height=""30"" width=""30"" url=""https://avatars.githubusercontent.com/u/74499432?s=30&amp;v=4""/>
    <content type=""html"">&lt;div class=&quot;issues_opened&quot;&gt;&lt;div class=&quot;body&quot;&gt;
&lt;!-- pull_request --&gt;
&lt;div class=&quot;d-flex flex-items-baseline border-bottom color-border-muted py-3&quot;&gt;
    &lt;span class=&quot;mr-2&quot;&gt;&lt;a class=&quot;d-inline-block&quot; href=&quot;/revolutionary-translation-bot&quot; rel=&quot;noreferrer&quot;&gt;&lt;img class=&quot;avatar avatar-user&quot; src=&quot;https://avatars.githubusercontent.com/u/74499432?s=64&amp;amp;v=4&quot; width=&quot;32&quot; height=&quot;32&quot; alt=&quot;@revolutionary-translation-bot&quot;&gt;&lt;/a&gt;&lt;/span&gt;
  &lt;div class=&quot;d-flex flex-column width-full&quot;&gt;
    &lt;div&gt;
      &lt;div class=&quot;d-flex flex-items-baseline&quot;&gt;
        &lt;div class=&quot;&quot;&gt;
              &lt;a class=&quot;Link--primary no-underline text-bold wb-break-all d-inline-block&quot; href=&quot;/revolutionary-translation-bot&quot; rel=&quot;noreferrer&quot;&gt;revolutionary-translation-bot&lt;/a&gt;
              opened
              a pull request in
              &lt;a class=&quot;Link--primary text-bold no-underline wb-break-all d-inline-block&quot; href=&quot;/Revolutionary-Games/Thrive&quot; rel=&quot;noreferrer&quot;&gt;Revolutionary-Games/Thrive&lt;/a&gt;
            &lt;span class=&quot;f6 color-fg-muted no-wrap ml-1&quot;&gt;
              &lt;relative-time datetime=&quot;2022-05-28T20:50:46Z&quot; class=&quot;no-wrap&quot;&gt;May 28, 2022&lt;/relative-time&gt;
            &lt;/span&gt;
        &lt;/div&gt;
      &lt;/div&gt;
    &lt;/div&gt;
    &lt;div class=&quot;Box p-3 my-2 &quot;&gt;
      &lt;svg height=&quot;16&quot; aria-label=&quot;Pull request&quot; class=&quot;octicon octicon-git-pull-request open d-inline-block mt-1 float-left&quot; viewBox=&quot;0 0 16 16&quot; version=&quot;1.1&quot; width=&quot;16&quot; role=&quot;img&quot;&gt;&lt;path fill-rule=&quot;evenodd&quot; d=&quot;M7.177 3.073L9.573.677A.25.25 0 0110 .854v4.792a.25.25 0 01-.427.177L7.177 3.427a.25.25 0 010-.354zM3.75 2.5a.75.75 0 100 1.5.75.75 0 000-1.5zm-2.25.75a2.25 2.25 0 113 2.122v5.256a2.251 2.251 0 11-1.5 0V5.372A2.25 2.25 0 011.5 3.25zM11 2.5h-1V4h1a1 1 0 011 1v5.628a2.251 2.251 0 101.5 0V5A2.5 2.5 0 0011 2.5zm1 10.25a.75.75 0 111.5 0 .75.75 0 01-1.5 0zM3.75 12a.75.75 0 100 1.5.75.75 0 000-1.5z&quot;&gt;&lt;/path&gt;&lt;/svg&gt;
      &lt;div class=&quot;ml-4&quot;&gt;
        &lt;div&gt;
          &lt;span class=&quot;f4 lh-condensed text-bold color-fg-default&quot;&gt;&lt;a class=&quot;color-fg-default text-bold&quot; aria-label=&quot;Translations update from Thrive - Weblate&quot; href=&quot;/Revolutionary-Games/Thrive/pull/3375&quot; rel=&quot;noreferrer&quot;&gt;Translations update from Thrive - Weblate&lt;/a&gt;&lt;/span&gt;
          &lt;span class=&quot;f4 color-fg-muted ml-1&quot;&gt;#3375&lt;/span&gt;
            &lt;div class=&quot;lh-condensed mb-2 mt-1&quot;&gt;
              &lt;p&gt;Translations update from &lt;a href=&quot;https://translate.revolutionarygamesstudio.com&quot; rel=&quot;nofollow noreferrer&quot;&gt;Thrive - Weblate&lt;/a&gt; for &lt;a href=&quot;https://translate.revolutionarygamesstudio.com/projects/thrive/thrive-game/&quot; rel=&quot;nofollow noreferrer&quot;&gt;Thrive/Thrive Game&lt;/a&gt;.
Current translation status:
&lt;/p&gt;
            &lt;/div&gt;
        &lt;/div&gt;
          &lt;div class=&quot;diffstat d-inline-block mt-1 tooltipped tooltipped-se&quot; aria-label=&quot;2 commits with 4 additions and 6 deletions&quot;&gt;
            &lt;span class=&quot;color-fg-success&quot;&gt;+4&lt;/span&gt;
            &lt;span class=&quot;color-fg-danger&quot;&gt;-6&lt;/span&gt;
          &lt;/div&gt;
      &lt;/div&gt;
    &lt;/div&gt;
  &lt;/div&gt;
&lt;/div&gt;
&lt;/div&gt;&lt;/div&gt;</content>
  </entry>
  <entry>
    <id>tag:github.com,2008:PullRequestReviewCommentEvent/22036523558</id>
    <published>2022-05-28T17:17:49Z</published>
    <updated>2022-05-28T17:17:49Z</updated>
    <link type=""text/html"" rel=""alternate"" href=""https://github.com/Revolutionary-Games/Thrive/pull/3374#discussion_r884158410""/>
    <title type=""html"">User4 commented on pull request Revolutionary-Games/Thrive#3374</title>
    <author>
      <name>User4</name>
      <uri>https://github.com/User4</uri>
    </author>
    <media:thumbnail height=""30"" width=""30"" url=""https://avatars.githubusercontent.com/u/3796411?s=30&amp;v=4""/>
    <content type=""html"">&lt;div class=&quot;issues_comment&quot;&gt;&lt;div class=&quot;body&quot;&gt;
&lt;!-- pull_request_review_comment --&gt;
&lt;div class=&quot;d-flex flex-items-baseline border-bottom color-border-muted py-3&quot;&gt;
      &lt;span class=&quot;mr-2&quot;&gt;&lt;a class=&quot;d-inline-block&quot; href=&quot;/User4&quot; rel=&quot;noreferrer&quot;&gt;&lt;img class=&quot;avatar avatar-user&quot; src=&quot;https://avatars.githubusercontent.com/u/3796411?s=64&amp;amp;v=4&quot; width=&quot;32&quot; height=&quot;32&quot; alt=&quot;@User4&quot;&gt;&lt;/a&gt;&lt;/span&gt;
  &lt;div class=&quot;d-flex flex-column width-full&quot;&gt;
      &lt;div class=&quot;d-flex flex-items-baseline mb-2&quot;&gt;
        &lt;div class=&quot;&quot;&gt;
          &lt;a class=&quot;Link--primary no-underline wb-break-all text-bold d-inline-block&quot; href=&quot;/User4&quot; rel=&quot;noreferrer&quot;&gt;User4&lt;/a&gt;
            commented on pull request
            &lt;a class=&quot;Link--primary text-bold&quot; title=&quot;Made toxins fire relative to cell's actual orientation&quot; href=&quot;https://github.com/Revolutionary-Games/Thrive/pull/3374#discussion_r884158410&quot; rel=&quot;noreferrer&quot;&gt;Revolutionary-Games/Thrive#3374&lt;/a&gt;
            &lt;span class=&quot;f6 color-fg-muted ml-1&quot;&gt;
              &lt;relative-time datetime=&quot;2022-05-28T17:17:49Z&quot; class=&quot;no-wrap&quot;&gt;May 28, 2022&lt;/relative-time&gt;
            &lt;/span&gt;
        &lt;/div&gt;
      &lt;/div&gt;
    &lt;div class=&quot;message markdown-body Box p-3 wb-break-all &quot;&gt;
      &lt;div class=&quot;f6 mb-1&quot;&gt;
        &lt;a class=&quot;Link--secondary&quot; title=&quot;Made toxins fire relative to cell's actual orientation&quot; href=&quot;https://github.com/Revolutionary-Games/Thrive/pull/3374#discussion_r884158410&quot; rel=&quot;noreferrer&quot;&gt;&lt;img class=&quot;avatar mr-1 avatar-user&quot; src=&quot;https://avatars.githubusercontent.com/u/3796411?s=32&amp;amp;v=4&quot; width=&quot;16&quot; height=&quot;16&quot; alt=&quot;@User4&quot;&gt; &lt;span class=&quot;Link--primary text-bold&quot;&gt;User4&lt;/span&gt; commented &lt;relative-time datetime=&quot;2022-05-28T17:17:49Z&quot; class=&quot;no-wrap&quot;&gt;May 28, 2022&lt;/relative-time&gt;&lt;/a&gt;
      &lt;/div&gt;
        &lt;p&gt;Don't use angles unless absolutely required, use quaternions instead. Also this doesn't get the global rotation, so this'll be incorrect in microbe…&lt;/p&gt;
    &lt;/div&gt;
  &lt;/div&gt;
&lt;/div&gt;
&lt;/div&gt;&lt;/div&gt;</content>
  </entry>
  <entry>
    <id>tag:github.com,2008:CommitCommentEvent/22034699503</id>
    <published>2022-05-28T11:34:42Z</published>
    <updated>2022-05-28T11:34:42Z</updated>
    <link type=""text/html"" rel=""alternate"" href=""https://github.com/Revolutionary-Games/Thrive/commit/2fc3f4d82c4de0e6d906a91f62f8d9f2b832c62c#r74792494""/>
    <title type=""html"">User4 commented on commit Revolutionary-Games/Thrive@2fc3f4d82c</title>
    <author>
      <name>User4</name>
      <uri>https://github.com/User4</uri>
    </author>
    <media:thumbnail height=""30"" width=""30"" url=""https://avatars.githubusercontent.com/u/3796411?s=30&amp;v=4""/>
    <content type=""html"">&lt;div class=&quot;commit_comment&quot;&gt;&lt;div class=&quot;body&quot;&gt;
&lt;!-- commit_comment --&gt;
&lt;div class=&quot;d-flex flex-items-baseline border-bottom color-border-muted py-3&quot;&gt;
    &lt;span class=&quot;mr-2&quot;&gt;&lt;a class=&quot;d-inline-block&quot; href=&quot;/User4&quot; rel=&quot;noreferrer&quot;&gt;&lt;img class=&quot;avatar avatar-user&quot; src=&quot;https://avatars.githubusercontent.com/u/3796411?s=64&amp;amp;v=4&quot; width=&quot;32&quot; height=&quot;32&quot; alt=&quot;@User4&quot;&gt;&lt;/a&gt;&lt;/span&gt;
  &lt;div class=&quot;d-flex flex-column width-full&quot;&gt;
    &lt;div class=&quot;d-flex flex-items-baseline mb-2&quot;&gt;
      &lt;div class=&quot;&quot;&gt;
        &lt;a class=&quot;Link--primary no-underline wb-break-all text-bold d-inline-block&quot; href=&quot;/User4&quot; rel=&quot;noreferrer&quot;&gt;User4&lt;/a&gt;
        commented on commit
        &lt;a class=&quot;Link--primary text-bold&quot; href=&quot;/Revolutionary-Games/Thrive/commit/2fc3f4d82c4de0e6d906a91f62f8d9f2b832c62c#r74792494&quot; rel=&quot;noreferrer&quot;&gt;Revolutionary-Games/Thrive@2fc3f4d82c&lt;/a&gt;
          &lt;span class=&quot;f6 color-fg-muted ml-1 no-wrap&quot;&gt;
            &lt;relative-time datetime=&quot;2022-05-28T11:34:42Z&quot; class=&quot;no-wrap&quot;&gt;May 28, 2022&lt;/relative-time&gt;
          &lt;/span&gt;
      &lt;/div&gt;
    &lt;/div&gt;
    &lt;div class=&quot;message markdown-body Box p-3 wb-break-all &quot;&gt;
      &lt;div class=&quot;f6 mb-1&quot;&gt;
        &lt;a class=&quot;Link--secondary&quot; href=&quot;/Revolutionary-Games/Thrive/commit/2fc3f4d82c4de0e6d906a91f62f8d9f2b832c62c#r74792494&quot; rel=&quot;noreferrer&quot;&gt;&lt;img class=&quot;avatar mr-1 avatar-user&quot; src=&quot;https://avatars.githubusercontent.com/u/3796411?s=32&amp;amp;v=4&quot; width=&quot;16&quot; height=&quot;16&quot; alt=&quot;@User4&quot;&gt; &lt;span class=&quot;Link--primary text-bold&quot;&gt;User4&lt;/span&gt; commented &lt;relative-time datetime=&quot;2022-05-28T11:34:42Z&quot; class=&quot;no-wrap&quot;&gt;May 28, 2022&lt;/relative-time&gt;&lt;/a&gt;
      &lt;/div&gt;
        &lt;p&gt;I think this step should be combined into CalculatePhotographDistance so there wouldn't be a need for the InstancedMesh property, which btw as it i…&lt;/p&gt;
    &lt;/div&gt;
  &lt;/div&gt;
&lt;/div&gt;
&lt;/div&gt;&lt;/div&gt;</content>
  </entry>
  <entry>
    <id>tag:github.com,2008:IssuesEvent/22033408761</id>
    <published>2022-05-28T07:25:55Z</published>
    <updated>2022-05-28T07:25:55Z</updated>
    <link type=""text/html"" rel=""alternate"" href=""https://github.com/Revolutionary-Games/Thrive/issues/3373""/>
    <title type=""html"">User4 opened an issue in Revolutionary-Games/Thrive</title>
    <author>
      <name>User4</name>
      <uri>https://github.com/User4</uri>
    </author>
    <media:thumbnail height=""30"" width=""30"" url=""https://avatars.githubusercontent.com/u/3796411?s=30&amp;v=4""/>
    <content type=""html"">&lt;div class=&quot;issues_opened&quot;&gt;&lt;div class=&quot;body&quot;&gt;
&lt;!-- issues --&gt;
&lt;div class=&quot;d-flex flex-items-baseline border-bottom color-border-muted py-3&quot;&gt;
    &lt;span class=&quot;mr-2&quot;&gt;&lt;a class=&quot;d-inline-block&quot; href=&quot;/User4&quot; rel=&quot;noreferrer&quot;&gt;&lt;img class=&quot;avatar avatar-user&quot; src=&quot;https://avatars.githubusercontent.com/u/3796411?s=64&amp;amp;v=4&quot; width=&quot;32&quot; height=&quot;32&quot; alt=&quot;@User4&quot;&gt;&lt;/a&gt;&lt;/span&gt;
  &lt;div class=&quot;d-flex flex-column width-full&quot;&gt;
    &lt;div class=&quot;d-flex flex-items-baseline mb-2&quot;&gt;
      &lt;div class=&quot;&quot;&gt;
          &lt;a class=&quot;Link--primary no-underline wb-break-all text-bold d-inline-block&quot; href=&quot;/User4&quot; rel=&quot;noreferrer&quot;&gt;User4&lt;/a&gt;
          opened
          an issue in
          &lt;a class=&quot;Link--primary no-underline wb-break-all text-bold d-inline-block&quot; href=&quot;/Revolutionary-Games/Thrive&quot; rel=&quot;noreferrer&quot;&gt;Revolutionary-Games/Thrive&lt;/a&gt;
          &lt;span class=&quot;f6 color-fg-muted no-wrap ml-1&quot;&gt;
            &lt;relative-time datetime=&quot;2022-05-28T07:25:55Z&quot; class=&quot;no-wrap&quot;&gt;May 28, 2022&lt;/relative-time&gt;
          &lt;/span&gt;
      &lt;/div&gt;
    &lt;/div&gt;
    &lt;div class=&quot;Box p-3 wb-break-all &quot;&gt;
      &lt;svg height=&quot;16&quot; aria-label=&quot;Issue&quot; class=&quot;octicon octicon-issue-opened open d-inline-block mt-1 float-left&quot; viewBox=&quot;0 0 16 16&quot; version=&quot;1.1&quot; width=&quot;16&quot; role=&quot;img&quot;&gt;&lt;path d=&quot;M8 9.5a1.5 1.5 0 100-3 1.5 1.5 0 000 3z&quot;&gt;&lt;/path&gt;&lt;path fill-rule=&quot;evenodd&quot; d=&quot;M8 0a8 8 0 100 16A8 8 0 008 0zM1.5 8a6.5 6.5 0 1113 0 6.5 6.5 0 01-13 0z&quot;&gt;&lt;/path&gt;&lt;/svg&gt;
      &lt;div class=&quot;ml-4&quot;&gt;
        &lt;span class=&quot;f4 lh-condensed text-bold color-fg-default&quot;&gt;
          &lt;a title=&quot;Auto-evo can get stuck in organelle mutations calculation&quot; class=&quot;color-fg-default&quot; aria-label=&quot;Auto-evo can get stuck in organelle mutations calculation&quot; href=&quot;/Revolutionary-Games/Thrive/issues/3373&quot; rel=&quot;noreferrer&quot;&gt;Auto-evo can get stuck in organelle mutations calculation&lt;/a&gt;
        &lt;/span&gt;
        &lt;span class=&quot;f4 color-fg-muted ml-1&quot;&gt;#3373&lt;/span&gt;
          &lt;div class=&quot;dashboard-break-word lh-condensed mb-2 mt-1&quot;&gt;
            &lt;p&gt;Seems like my fix to the islands fixing code can still get stuck in some cases.
My guess as to how this could be fixed: what if the code just doesn…&lt;/p&gt;
          &lt;/div&gt;
      &lt;/div&gt;
    &lt;/div&gt;
  &lt;/div&gt;
&lt;/div&gt;
&lt;/div&gt;&lt;/div&gt;</content>
  </entry>
  <entry>
    <id>tag:github.com,2008:PullRequestEvent/22023431903</id>
    <published>2022-05-27T14:08:04Z</published>
    <updated>2022-05-27T14:08:04Z</updated>
    <link type=""text/html"" rel=""alternate"" href=""https://github.com/Revolutionary-Games/Thrive/pull/3339""/>
    <title type=""html"">User4 merged a pull request in Revolutionary-Games/Thrive</title>
    <author>
      <name>User4</name>
      <uri>https://github.com/User4</uri>
    </author>
    <media:thumbnail height=""30"" width=""30"" url=""https://avatars.githubusercontent.com/u/3796411?s=30&amp;v=4""/>
    <content type=""html"">&lt;div class=&quot;issues_merged&quot;&gt;&lt;div class=&quot;body&quot;&gt;
&lt;!-- pull_request --&gt;
&lt;div class=&quot;d-flex flex-items-baseline border-bottom color-border-muted py-3&quot;&gt;
    &lt;span class=&quot;mr-2&quot;&gt;&lt;a class=&quot;d-inline-block&quot; href=&quot;/User4&quot; rel=&quot;noreferrer&quot;&gt;&lt;img class=&quot;avatar avatar-user&quot; src=&quot;https://avatars.githubusercontent.com/u/3796411?s=64&amp;amp;v=4&quot; width=&quot;32&quot; height=&quot;32&quot; alt=&quot;@User4&quot;&gt;&lt;/a&gt;&lt;/span&gt;
  &lt;div class=&quot;d-flex flex-column width-full&quot;&gt;
    &lt;div&gt;
      &lt;div class=&quot;d-flex flex-items-baseline&quot;&gt;
        &lt;div class=&quot;&quot;&gt;
              &lt;a class=&quot;Link--primary no-underline text-bold wb-break-all d-inline-block&quot; href=&quot;/User4&quot; rel=&quot;noreferrer&quot;&gt;User4&lt;/a&gt;
              merged
              a pull request in
              &lt;a class=&quot;Link--primary text-bold no-underline wb-break-all d-inline-block&quot; href=&quot;/Revolutionary-Games/Thrive&quot; rel=&quot;noreferrer&quot;&gt;Revolutionary-Games/Thrive&lt;/a&gt;
            &lt;span class=&quot;f6 color-fg-muted no-wrap ml-1&quot;&gt;
              &lt;relative-time datetime=&quot;2022-05-27T14:08:04Z&quot; class=&quot;no-wrap&quot;&gt;May 27, 2022&lt;/relative-time&gt;
            &lt;/span&gt;
        &lt;/div&gt;
      &lt;/div&gt;
    &lt;/div&gt;
    &lt;div class=&quot;Box p-3 my-2 &quot;&gt;
      &lt;svg height=&quot;16&quot; aria-label=&quot;Pull request&quot; class=&quot;octicon octicon-git-pull-request merged d-inline-block mt-1 float-left&quot; viewBox=&quot;0 0 16 16&quot; version=&quot;1.1&quot; width=&quot;16&quot; role=&quot;img&quot;&gt;&lt;path fill-rule=&quot;evenodd&quot; d=&quot;M7.177 3.073L9.573.677A.25.25 0 0110 .854v4.792a.25.25 0 01-.427.177L7.177 3.427a.25.25 0 010-.354zM3.75 2.5a.75.75 0 100 1.5.75.75 0 000-1.5zm-2.25.75a2.25 2.25 0 113 2.122v5.256a2.251 2.251 0 11-1.5 0V5.372A2.25 2.25 0 011.5 3.25zM11 2.5h-1V4h1a1 1 0 011 1v5.628a2.251 2.251 0 101.5 0V5A2.5 2.5 0 0011 2.5zm1 10.25a.75.75 0 111.5 0 .75.75 0 01-1.5 0zM3.75 12a.75.75 0 100 1.5.75.75 0 000-1.5z&quot;&gt;&lt;/path&gt;&lt;/svg&gt;
      &lt;div class=&quot;ml-4&quot;&gt;
        &lt;div&gt;
          &lt;span class=&quot;f4 lh-condensed text-bold color-fg-default&quot;&gt;&lt;a class=&quot;color-fg-default text-bold&quot; aria-label=&quot;Translations update from Thrive - Weblate&quot; href=&quot;/Revolutionary-Games/Thrive/pull/3339&quot; rel=&quot;noreferrer&quot;&gt;Translations update from Thrive - Weblate&lt;/a&gt;&lt;/span&gt;
          &lt;span class=&quot;f4 color-fg-muted ml-1&quot;&gt;#3339&lt;/span&gt;
            &lt;div class=&quot;lh-condensed mb-2 mt-1&quot;&gt;
              &lt;p&gt;Translations update from &lt;a href=&quot;https://translate.revolutionarygamesstudio.com&quot; rel=&quot;nofollow noreferrer&quot;&gt;Thrive - Weblate&lt;/a&gt; for &lt;a href=&quot;https://translate.revolutionarygamesstudio.com/projects/thrive/thrive-game/&quot; rel=&quot;nofollow noreferrer&quot;&gt;Thrive/Thrive Game&lt;/a&gt;.
Current translation status:
&lt;/p&gt;
            &lt;/div&gt;
        &lt;/div&gt;
          &lt;div class=&quot;diffstat d-inline-block mt-1 tooltipped tooltipped-se&quot; aria-label=&quot;8 commits with 6,677 additions and 1,596 deletions&quot;&gt;
            &lt;span class=&quot;color-fg-success&quot;&gt;+6,677&lt;/span&gt;
            &lt;span class=&quot;color-fg-danger&quot;&gt;-1,596&lt;/span&gt;
          &lt;/div&gt;
      &lt;/div&gt;
    &lt;/div&gt;
  &lt;/div&gt;
&lt;/div&gt;
&lt;/div&gt;&lt;/div&gt;</content>
  </entry>
</feed>";

    private const string RemapToHtmlInput = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<feed xmlns=""http://www.w3.org/2005/Atom"" xmlns:media=""http://search.yahoo.com/mrss/"" xml:lang=""en-US"">
  <id>tag:github.com,2008:/organizations/Revolutionary-Games/user</id>
  <link type=""text/html"" rel=""alternate"" href=""https://github.com/organizations/Revolutionary-Games/user""/>
  <link type=""application/atom+xml"" rel=""self"" href=""https://github.com/organizations/Revolutionary-Games/user.private.atom?token=abcd""/>
  <title>Private Feed for the Revolutionary-Games Organization</title>
  <updated>2022-05-29T13:39:32Z</updated>
  <entry>
    <id>tag:github.com,2008:CreateEvent/22041400336</id>
    <published>2022-05-29T12:12:03Z</published>
    <updated>2022-05-29T12:12:03Z</updated>
    <link type=""text/html"" rel=""alternate"" href=""https://github.com/Revolutionary-Games/Thrive/compare/improve_chemoreceptor_visuals""/>
    <title type=""html"">User2 created a branch improve_chemoreceptor_visuals in Revolutionary-Games/Thrive</title>
    <author>
      <name>User2</name>
      <uri>https://github.com/User2</uri>
    </author>
    <media:thumbnail height=""30"" width=""30"" url=""https://avatars.githubusercontent.com/u/7430433?s=30&amp;v=4""/>
    <content type=""html"">&lt;div class=&quot;git-branch&quot;&gt;&lt;div class=&quot;body&quot;&gt;
&lt;!-- create --&gt;
&lt;div class=&quot;d-flex flex-items-baseline border-bottom color-border-muted py-3&quot;&gt;
      &lt;span class=&quot;mr-2&quot;&gt;&lt;a class=&quot;d-inline-block&quot; href=&quot;/User2&quot; rel=&quot;noreferrer&quot;&gt;&lt;img class=&quot;avatar avatar-user&quot; src=&quot;https://avatars.githubusercontent.com/u/7430433?s=64&amp;amp;v=4&quot; width=&quot;32&quot; height=&quot;32&quot; alt=&quot;@User2&quot;&gt;&lt;/a&gt;&lt;/span&gt;
  &lt;div class=&quot;d-flex flex-column width-full&quot;&gt;
        &lt;div class=&quot;d-flex flex-items-baseline&quot;&gt;
          &lt;div class=&quot;&quot;&gt;
              &lt;a class=&quot;Link--primary no-underline wb-break-all text-bold d-inline-block&quot; href=&quot;/User2&quot; rel=&quot;noreferrer&quot;&gt;User2&lt;/a&gt;
              created a
              branch
              in
              &lt;a class=&quot;Link--primary no-underline wb-break-all text-bold d-inline-block&quot; href=&quot;/Revolutionary-Games/Thrive&quot; rel=&quot;noreferrer&quot;&gt;Revolutionary-Games/Thrive&lt;/a&gt;
              &lt;span class=&quot;f6 color-fg-muted no-wrap ml-1&quot;&gt;
                &lt;relative-time datetime=&quot;2022-05-29T12:12:03Z&quot; class=&quot;no-wrap&quot;&gt;May 29, 2022&lt;/relative-time&gt;
              &lt;/span&gt;
          &lt;/div&gt;
        &lt;/div&gt;
    &lt;div class=&quot;Box p-3 mt-2 &quot;&gt;
      &lt;div&gt;
        &lt;div class=&quot;f4 lh-condensed text-bold color-fg-default&quot;&gt;
              &lt;a class=&quot;css-truncate css-truncate-target branch-name v-align-middle&quot; title=&quot;improve_chemoreceptor_visuals&quot; href=&quot;/Revolutionary-Games/Thrive/tree/improve_chemoreceptor_visuals&quot; rel=&quot;noreferrer&quot;&gt;improve_chemoreceptor_visuals&lt;/a&gt; in &lt;a class=&quot;Link--primary text-bold no-underline wb-break-all d-inline-block&quot; href=&quot;/Revolutionary-Games/Thrive&quot; rel=&quot;noreferrer&quot;&gt;Revolutionary-Games/Thrive&lt;/a&gt;
        &lt;/div&gt;
          &lt;p class=&quot;f6 color-fg-muted mt-2 mb-0&quot;&gt;
            &lt;span&gt;Updated May 29&lt;/span&gt;
          &lt;/p&gt;
      &lt;/div&gt;
    &lt;/div&gt;
  &lt;/div&gt;
&lt;/div&gt;
&lt;/div&gt;&lt;/div&gt;</content>
  </entry>
</feed>";

    private const string RemapToHtmlOutput = @"<div class=""custom-feed-item-class feed-test"">
<span class=""custom-feed-icon-test""></span>
<span class=""custom-feed-title""><span class=""custom-feed-title-main"">
<a class=""custom-feed-title-link"" href=""https://github.com/Revolutionary-Games/Thrive/compare/improve_chemoreceptor_visuals"">User2 created a branch improve_chemoreceptor_visuals in Revolutionary-Games/Thrive</a>
</span><span class=""custom-feed-by""> by
<span class=""custom-feed-author"">User2</span></span><span class=""custom-feed-at""> at <span class=""custom-feed-time"">2022-29-05 15.12</span></span>
</span><br><span class=""custom-feed-content""><div class=""git-branch""><div class=""body"">
<!-- create -->
<div class=""d-flex flex-items-baseline border-bottom color-border-muted py-3"">
      <span class=""mr-2""><a class=""d-inline-block"" href=""/User2"" rel=""noreferrer""><img class=""avatar avatar-user"" src=""https://avatars.githubusercontent.com/u/7430433?s=64&amp;v=4"" width=""32"" height=""32"" alt=""@User2""></a></span>
  <div class=""d-flex flex-column width-full"">
        <div class=""d-flex flex-items-baseline"">
          <div class="""">
              <a class=""Link--primary no-underline wb-break-all text-bold d-inline-block"" href=""/User2"" rel=""noreferrer"">User2</a>
              created a
              branch
              in
              <a class=""Link--primary no-underline wb-break-all text-bold d-inline-block"" href=""/Revolutionary-Games/Thrive"" rel=""noreferrer"">Revolutionary-Games/Thrive</a>
              <span class=""f6 color-fg-muted no-wrap ml-1"">
                <relative-time datetime=""2022-05-29T12:12:03Z"" class=""no-wrap"">May 29, 2022</relative-time>
              </span>
          </div>
        </div>
    <div class=""Box p-3 mt-2 "">
      <div>
        <div class=""f4 lh-condensed text-bold color-fg-default"">
              <a class=""css-truncate css-truncate-target branch-name v-align-middle"" title=""improve_chemoreceptor_visuals"" href=""/Revolutionary-Games/Thrive/tree/improve_chemoreceptor_visuals"" rel=""noreferrer"">improve_chemoreceptor_visuals</a> in <a class=""Link--primary text-bold no-underline wb-break-all d-inline-block"" href=""/Revolutionary-Games/Thrive"" rel=""noreferrer"">Revolutionary-Games/Thrive</a>
        </div>
          <p class=""f6 color-fg-muted mt-2 mb-0"">
            <span>Updated May 29</span>
          </p>
      </div>
    </div>
  </div>
</div>
</div></div><br><a class=""custom-feed-item-url"" href=""https://github.com/Revolutionary-Games/Thrive/compare/improve_chemoreceptor_visuals"">Read it here</a></span></div>
</div>";

    [Fact]
    public static void Feed_TitleReplaceTextParts()
    {
        var feed = new Feed("test", "test", TimeSpan.FromMinutes(1))
        {
            PreprocessingActions = new List<FeedPreprocessingAction>()
            {
                // Regexes as strings
                // ReSharper disable StringLiteralTypo
                new(PreprocessingActionTarget.Title, @"[\w-_]+\scommented", "New comment"),
                new(PreprocessingActionTarget.Title, @"[\w-_]+\sclosed an issue", "Issue closed"),
                new(PreprocessingActionTarget.Title, @"[\w-_]+\sopened a pull request", "New pull request"),
                new(PreprocessingActionTarget.Title, @"[\w-_]+\sforked .+ from", "New fork of"),
                new(PreprocessingActionTarget.Title, @"[\w-_]+\spushed", "New commits"),
                new(PreprocessingActionTarget.Title, @"[\w-_]+\sopened an issue",
                    "New issue"),
                new(PreprocessingActionTarget.Summary,
                    @"data-(hydro|ga|)-click[\w\-]*=""[^""]*", ""),
                new(PreprocessingActionTarget.Summary, "<svg .*>.*</svg>", ""),

                // ReSharper restore StringLiteralTypo
            },
        };

        var items = feed.ProcessContent(TestGithubFeedContent).ToList();

        Assert.NotEmpty(items);
        Assert.NotNull(feed.ContentUpdatedAt);
        Assert.NotNull(feed.LatestContent);
        Assert.NotEmpty(feed.LatestContent!);
        Assert.Equal(7, items.Count);

        Assert.Equal("User2", items[0].Author);
        Assert.Equal("tag:github.com,2008:CreateEvent/22041400336", items[0].Id);
        Assert.Equal("User2 created a branch improve_chemoreceptor_visuals in Revolutionary-Games/Thrive",
            items[0].Title);
        Assert.Equal("https://github.com/Revolutionary-Games/Thrive/compare/improve_chemoreceptor_visuals",
            items[0].Link);
        Assert.NotNull(items[0].Summary);
        Assert.NotEmpty(items[0].Summary!);
        Assert.Equal(DateTime.Parse("2022-05-29T12:12:03Z"), items[0].PublishedAt);

        Assert.Equal("User3", items[1].Author);
        Assert.Equal("tag:github.com,2008:PushEvent/22041370895", items[1].Id);
        Assert.Equal("New commits to art_gallery in Revolutionary-Games/Thrive", items[1].Title);
        Assert.Equal("https://github.com/Revolutionary-Games/Thrive/compare/2fc3f4d82c...dd5375c068", items[1].Link);
        Assert.NotNull(items[1].Summary);
        Assert.NotEmpty(items[1].Summary!);

        Assert.Equal("revolutionary-translation-bot", items[2].Author);
        Assert.Equal("tag:github.com,2008:PullRequestEvent/22037544822", items[2].Id);
        Assert.Equal("New pull request in Revolutionary-Games/Thrive", items[2].Title);
        Assert.Equal("https://github.com/Revolutionary-Games/Thrive/pull/3375", items[2].Link);
        Assert.NotNull(items[2].Summary);
        Assert.NotEmpty(items[2].Summary!);

        Assert.Equal("User4", items[3].Author);
        Assert.Equal("tag:github.com,2008:PullRequestReviewCommentEvent/22036523558", items[3].Id);
        Assert.Equal("New comment on pull request Revolutionary-Games/Thrive#3374", items[3].Title);
        Assert.Equal("https://github.com/Revolutionary-Games/Thrive/pull/3374#discussion_r884158410", items[3].Link);
        Assert.NotNull(items[3].Summary);
        Assert.NotEmpty(items[3].Summary!);

        Assert.Equal("User4", items[4].Author);
        Assert.Equal("tag:github.com,2008:CommitCommentEvent/22034699503", items[4].Id);
        Assert.Equal("New comment on commit Revolutionary-Games/Thrive@2fc3f4d82c", items[4].Title);
        Assert.Equal(
            "https://github.com/Revolutionary-Games/Thrive/commit/2fc3f4d82c4de0e6d906a91f62f8d9f2b832c62c#r74792494",
            items[4].Link);
        Assert.NotNull(items[4].Summary);
        Assert.NotEmpty(items[4].Summary!);

        Assert.Equal("User4", items[5].Author);
        Assert.Equal("tag:github.com,2008:IssuesEvent/22033408761", items[5].Id);
        Assert.Equal("New issue in Revolutionary-Games/Thrive", items[5].Title);
        Assert.Equal("https://github.com/Revolutionary-Games/Thrive/issues/3373", items[5].Link);
        Assert.NotNull(items[5].Summary);
        Assert.NotEmpty(items[5].Summary!);

        Assert.Equal("User4", items[6].Author);
        Assert.Equal("tag:github.com,2008:PullRequestEvent/22023431903", items[6].Id);
        Assert.Equal("User4 merged a pull request in Revolutionary-Games/Thrive", items[6].Title);
        Assert.Equal("https://github.com/Revolutionary-Games/Thrive/pull/3339", items[6].Link);
        Assert.NotNull(items[6].Summary);
        Assert.NotEmpty(items[6].Summary!);
    }

    [Fact]
    public static void Feed_RemapToHtmlCorrectResult()
    {
        var feed = new Feed("test", "test", TimeSpan.FromMinutes(1))
        {
            HtmlFeedItemEntryTemplate = @"<div class=""custom-feed-item-class feed-{FeedName}"">
<span class=""custom-feed-icon-{OriginalFeedName}""></span>
<span class=""custom-feed-title""><span class=""custom-feed-title-main"">
<a class=""custom-feed-title-link"" href=""{Link}"">{Title}</a>
</span><span class=""custom-feed-by""> by
<span class=""custom-feed-author"">{AuthorFirstWord}</span></span><span class=""custom-feed-at""> at <span class=""custom-feed-time"">{PublishedAt:yyyy-dd-MM HH.mm}</span></span>
</span><br><span class=""custom-feed-content"">{Summary}<br><a class=""custom-feed-item-url"" href=""{Link}"">Read it here</a></span></div>
</div>",
        };

        feed.ProcessContent(RemapToHtmlInput);

        Assert.Equal(RemapToHtmlOutput, feed.HtmlLatestContent);

        // Check that it is valid HTML
        var parser = new HtmlParser();
        parser.ParseDocument(feed.HtmlLatestContent!);
    }
}

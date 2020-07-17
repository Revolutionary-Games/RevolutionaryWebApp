# frozen_string_literal: true

class DevBuildItem < HyperComponent
  include Hyperstack::Router::Helpers

  param :item

  render(TR) do
    TH(scope: 'row') { Link("/builds/#{@Item.id}") { @Item.id.to_s } }
    TD { @Item.build_hash.to_s }
    TD { @Item.platform.to_s }
    TD { @Item.verified.to_s }
    TD { @Item.score.to_s }
    TD { (@Item.description&.slice 0, 30).to_s }
    TD { @Item.pr_url.to_s }
    TD { @Item.created_at.to_s }
    TD { @Item.downloads.to_s }
    TD { (!@Item.important && !@Item.keep).to_s }
  end
end

# Shows devbuilds
class DevBuilds < HyperComponent
  include Hyperstack::Router::Helpers

  before_mount do
    @trusted_page = 0
    @untrusted_page = 0
  end

  render(DIV) do
    H2 { 'DevBuilds' }

    P {
      'DevBuilds are preview versions of Thrive with new features and fixes that have been' \
      ' done after the previous release.'
    }

    unless App.acting_user
      P { 'Please login to access the devbuilds.' }
      return
    end

    H3 { 'Trusted Builds' }
    P {
      'These devbuilds are created from each commit in the official Thrive repository, '\
      'and are safe as long as the repository security is not compromised.'
    }

    items = DevBuild.non_anonymous.sort_by_created_at

    Paginator(current_page: @trusted_page,
              page_size: 30,
              item_count: items.count,
              ref: set(:verified_paginator),
              key: 'verified_paginator') {
      # This is set with a delay
      if @verified_paginator
        RS.Table(:striped, :responsive) {
          THEAD {
            TR {
              TH { 'ID' }
              TH { 'Commit' }
              TH { 'Platform' }
              TH { 'Verified' }
              TH { 'Score' }
              TH { 'Description' }
              TH { 'PR' }
              TH { 'Created' }
              TH { 'Downloads' }
              TH { 'Deleted after 90 days' }
            }
          }
          TBODY {
            items.paginated(@verified_paginator.offset,
                            @verified_paginator.take_count).each { |item|
              DevBuildItem(item: item)
            }
          }
        }
      end
    }.on(:page_changed) { |page|
      mutate { @trusted_page = page }
    }.on(:created) {
      mutate {}
    }

    BR {}
    H3 { 'Untrusted Builds' }
    P {
      'These devbuilds have been uploaded without authentication. As such these files should' \
      ' not be trusted without scrutiny. For example pull requests from people outside ' \
      'the team show up here. The verified column says true, when a Thrive developer has ' \
      'verified the build.'
    }

    anon_items = DevBuild.anonymous.sort_by_created_at

    Paginator(current_page: @untrusted_page,
              page_size: 15,
              item_count: anon_items.count,
              ref: set(:anon_paginator),
              key: 'anon_paginator') {
      # This is set with a delay
      if @anon_paginator
        RS.Table(:striped, :responsive) {
          THEAD {
            TR {
              TH { 'ID' }
              TH { 'Commit' }
              TH { 'Platform' }
              TH { 'Verified' }
              TH { 'Score' }
              TH { 'Description' }
              TH { 'PR' }
              TH { 'Created' }
              TH { 'Downloads' }
              TH { 'Deleted after 90 days' }
            }
          }
          TBODY {
            anon_items.paginated(@anon_paginator.offset,
                                 @anon_paginator.take_count).each { |item|
              DevBuildItem(item: item)
            }
          }
        }
      end
    }.on(:page_changed) { |page|
      mutate { @trusted_page = page }
    }.on(:created) {
      mutate {}
    }
  end
end

# frozen_string_literal: true

# Shows devbuilds
class DevBuilds < HyperComponent
  include Hyperstack::Router::Helpers
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

    RS.Table(:striped, :responsive) {
      THEAD {
        TR {
          TH { 'ID' }
          TH { 'Commit' }
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
      }
    }

    BR {}
    H3 { 'Untrusted Builds' }
    P {
      'These devbuilds have been uploaded without authentication. As such these files should' \
      ' not be trusted without scrutiny. For example pull requests from people outside ' \
      'the team show up here. The verified column says true, when a Thrive developer has ' \
      'verified the build.'
    }

    RS.Table(:striped, :responsive) {
      THEAD {
        TR {
          TH { 'ID' }
          TH { 'Commit' }
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
      }
    }
  end
end

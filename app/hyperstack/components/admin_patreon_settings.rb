# frozen_string_literal: true

#  Patreon settings for admins
class AdminPatreonSettings < HyperComponent
  include Hyperstack::Router::Helpers

  def find_existing_settings
    mutate @loading_finished = false

    Hyperstack::Model.load do
      PatreonSettings.take(1).first
    end.then { |result|
      mutate {
        @settings = result
        @loading_finished = true
      }
    }
  end

  before_mount do
    find_existing_settings
    @updated_options = false
    @creating_new = false
    @show_token = false
    @show_refresh = false
    @operation_status = nil
    @show_spinner = false
    @show_webhook = false
    @show_fetching_spinner = false
    @find_campaign_id_status = nil
  end

  render(DIV) do
    H3 { 'Patreon settings' }

    unless @loading_finished
      P { 'Loading' }
      return
    end

    if @settings.nil?
      @settings = PatreonSettings.new
      @settings.active = true
      @settings.devbuilds_pledge_cents = 500
      @settings.vip_pledge_cents = 1500
      @creating_new = true
    end

    RS.Form() {
      RS.FormGroup(:check) {
        RS.Label(:check) {
          INPUT(type: :checkbox, checked: @settings.active).on(:change) { |e|
            mutate {
              @updated_options = true
              @settings.active = e.target.checked
            }
          }
          SPAN { ' Enabled' }
        }
      }

      RS.FormGroup {
        RS.Label { 'Patreon API access' }
        BR {}
        INPUT(type: @show_token ? :text : :password, placeholder: 'creator token',
              name: 'creator_token',
              value: @settings.creator_token)
          .on(:change) { |e|
          mutate {
            @updated_options = true
            @settings.creator_token = e.target.value
          }
        }
        BR {}
        INPUT(type: @show_refresh ? :text : :password, placeholder: 'refresh token',
              name: 'creator_refresh_token',
              value: @settings.creator_refresh_token)
          .on(:change) { |e|
          mutate {
            @updated_options = true
            @settings.creator_refresh_token = e.target.value
          }
        }
      }

      RS.FormGroup {
        RS.Label { 'Campaign' }
        BR {}
        INPUT(type: :text, placeholder: 'campaign id', name: 'campaign_id',
              value: @settings.campaign_id)
          .on(:change) { |e|
          mutate {
            @updated_options = true
            @settings.campaign_id = e.target.value
          }
        }
        BR {}
        if @find_campaign_id_status
          SPAN {
            @find_campaign_id_status
          }
          BR {}
        end
        RS.Button(color: 'primary', disabled: @creating_new) {
          SPAN { 'Fetch Campaign ID (after creation)' }
          RS.Spinner(color: 'secondary', size: 'sm') if @show_fetching_spinner
        }.on(:click) {
          mutate {
            @show_fetching_spinner = true
            @find_campaign_id_status = nil
          }

          FindPatreonCampaignId.run(patreon_settings_id: @settings.id)
                               .then { |campaign_id|
            mutate {
              @find_campaign_id_status = nil
              @show_fetching_spinner = false
              @settings.campaign_id = campaign_id
            }
          }.fail { |error|
            mutate {
              @find_campaign_id_status = "Failed to find campaign id, error: #{error}"
              @show_fetching_spinner = false
            }
          }
        }
      }

      RS.FormGroup {
        RS.Label { 'Rewards' }
        BR {}
        RS.Label { 'Devbuilds cents: ' }
        INPUT(type: :numeric, placeholder: 'devbuilds pledge cents',
              name: 'devbuilds_pledge_cents',
              value: @settings.devbuilds_pledge_cents)
          .on(:change) { |e|
          mutate {
            @updated_options = true
            @settings.devbuilds_pledge_cents = e.target.value
          }
        }
        BR {}
        RS.Label { 'VIP pledge cents: ' }
        INPUT(type: :numeric, placeholder: 'VIP pledge cents', name: 'vip_pledge_cents',
              value: @settings.vip_pledge_cents)
          .on(:change) { |e|
          mutate {
            @updated_options = true
            @settings.vip_pledge_cents = e.target.value
          }
        }

        RS.FormGroup {
          RS.Label { 'Patreon webhooks' }
          BR {}
          INPUT(type: @show_webhook ? :text : :password, placeholder: 'webhook secret',
                name: 'webhook_secret',
                value: @settings.webhook_secret)
            .on(:change) { |e|
            mutate {
              @updated_options = true
              @settings.webhook_secret = e.target.value
            }
          }
        }
      }
    }

    RS.Spinner(color: 'primary') if @show_spinner

    if @operation_status
      SPAN {
        @operation_status
      }
    end

    BR {} if @show_spinner || @operation_status

    if @creating_new
      can_create = @updated_options

      if can_create
        if @settings.creator_token.blank? || @settings.creator_refresh_token.blank? ||
           !@settings.devbuilds_pledge_cents || !@settings.vip_pledge_cents
          can_create = false
        end
      end

      RS.Button(color: 'primary', disabled: !can_create) { 'Create' }.on(:click) {
        mutate {
          @show_spinner = true
          @operation_status = 'Creating patreon settings'
        }

        CreatePatreonSettings.run(active: @settings.active,
                                  creator_token: @settings.creator_token,
                                  creator_refresh_token: @settings.creator_refresh_token,
                                  campaign_id: @settings.campaign_id,
                                  webhook_secret: @settings.webhook_secret,
                                  devbuilds_pledge_cents: @settings.devbuilds_pledge_cents,
                                  vip_pledge_cents: @settings.vip_pledge_cents)
                             .then {
          mutate {
            @show_spinner = false
            @operation_status = 'Settings created'
            @creating_new = false
            find_existing_settings
          }
        }.fail { |error|
          mutate {
            @show_spinner = false
            @operation_status = "Failed to create settings, error: #{error}"
          }
        }
      }
    else
      RS.Button(color: 'primary', disabled: !@updated_options) { 'Update' }.on(:click) {
        mutate {
          @show_spinner = true
          @operation_status = 'Updating patreon settings'
        }

        @settings.save.then {
          mutate {
            @show_spinner = false
            @operation_status = 'Settings updated'
          }
        }.fail { |error|
          @show_spinner = false
          @operation_status = "Failed to update settings, error: #{error}"
        }
      }

      RS.Button(color: 'danger') { 'Delete' }.on(:click) {
        mutate {
          @show_spinner = true
          @operation_status = 'DELETING Patreon settings'
        }

        DeletePatreonSettings.run(patreon_settings_id: @settings.id)
                             .then {
          mutate {
            @show_spinner = false
            @operation_status = 'Settings deleted'
            @creating_new = true
            @settings = nil
          }
        }.fail { |error|
          mutate {
            @show_spinner = false
            @operation_status = "Failed to create delete settings, error: #{error}"
          }
        }
      }

      BR {}
      BR {}

      P {
        "Last refreshed all patrons: #{@settings.last_refreshed} " \
        "last webhook: #{@settings.last_webhook}"
      }

    end
  end
end

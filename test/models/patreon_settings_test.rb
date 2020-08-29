# frozen_string_literal: true

require 'test_helper'

class PatreonSettingsTest < ActiveSupport::TestCase
  test 'can create one with valid settings' do
    PatreonSettings.create!(active: true, creator_token: 'aa',
                            creator_refresh_token: 'bb')

    PatreonSettings.create!(active: true, creator_token: 'aa2',
                            creator_refresh_token: 'bb2', devbuilds_reward_id: "123",
                            vip_reward_id: "124")
  end

  test "can't create one with vip pledge the same" do
    assert_raise ActiveRecord::RecordInvalid do
      PatreonSettings.create!(active: true, creator_token: 'aa',
                              creator_refresh_token: 'bb', devbuilds_reward_id: "123",
                              vip_reward_id: "123")
    end
  end
end

# frozen_string_literal: true

require 'test_helper'

class PatreonSettingsTest < ActiveSupport::TestCase
  test 'can create one with valid settings' do
    PatreonSettings.create!(active: true, creator_token: 'aa',
                            creator_refresh_token: 'bb', devbuilds_pledge_cents: 500,
                            vip_pledge_cents: 1000)
  end

  test "can't create one with vip pledge higher" do
    assert_raise ActiveRecord::RecordInvalid do
      PatreonSettings.create!(active: true, creator_token: 'aa',
                              creator_refresh_token: 'bb', devbuilds_pledge_cents: 15,
                              vip_pledge_cents: 10)
    end
  end

  test "can't create without pledges" do
    assert_raise StandardError do
      PatreonSettings.create!(active: true, creator_token: 'aa',
                              creator_refresh_token: 'bb', devbuilds_pledge_cents: 500)
    end

    assert_raise StandardError do
      PatreonSettings.create!(active: true, creator_token: 'aa',
                              creator_refresh_token: 'bb', vip_pledge_cents: 1500)
    end

    assert_raise StandardError do
      PatreonSettings.create!(active: true, creator_token: 'aa',
                              creator_refresh_token: 'bb')
    end
  end

  # test "the truth" do
  #   assert true
  # end
end
